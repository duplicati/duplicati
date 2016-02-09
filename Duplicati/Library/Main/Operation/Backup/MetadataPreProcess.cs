//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using CoCoL;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Common;

namespace Duplicati.Library.Main.Operation.Backup
{
    public class MetadataPreProcess : ProcessHelper
    {
        public struct FileEntry
        {
            public long OldId;
            public string Path;
            public DateTime OldModified;
            public long LastFileSize;
            public string OldMetaHash;
            public long OldMetaSize;
            public DateTime LastWrite;
            public FileAttributes Attributes;
        }

        [ChannelName("SourcePaths")]
        private IReadChannel<string> m_input;

        [ChannelName("LogChannel")]
        private IWriteChannel<LogMessage> m_logchannel;

        [ChannelName("ProgressChannel")]
        private IWriteChannel<ProgressEvent> m_progresschannel;

        [ChannelName("ProcessedFiles")]
        private IWriteChannel<FileEntry> m_output;

        [ChannelName("OutputBlocks")]
        private IWriteChannel<DataBlock> m_blockoutput;


        private Snapshots.ISnapshotService m_snapshot;
        private Options m_options;
        private BackupDatabase m_database;
        private readonly IMetahash EMPTY_METADATA;
        private int m_blocksize;

        public MetadataPreProcess(Snapshots.ISnapshotService snapshot, Options options, BackupDatabase database)
            : base()
        {
            m_snapshot = snapshot;
            m_options = options;
            m_database = database;
            m_blocksize = m_options.Blocksize;
            EMPTY_METADATA = Utility.WrapMetadata(new Dictionary<string, string>(), options);
        }

        private async Task<bool> ProcessMetadata(string path, FileAttributes attributes, DateTime lastwrite)
        {
            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                {
                    await m_logchannel.WriteAsync(LogMessage.Verbose("Ignoring symlink {0}", path));
                    return false;
                }

                if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Store)
                {
                    var metadata = await MetadataGenerator.GenerateMetadataAsync(path, attributes, m_options, m_snapshot, m_logchannel);

                    if (!metadata.ContainsKey("CoreSymlinkTarget"))
                        metadata["CoreSymlinkTarget"] = m_snapshot.GetSymlinkTarget(path);

                    var metahash = Utility.WrapMetadata(metadata, m_options);
                    AddSymlinkToOutput(path, DateTime.UtcNow, metahash);

                    await m_logchannel.WriteAsync(LogMessage.Verbose("Stored symlink {0}", path));
                    // Don't process further
                    return false;
                }
            }

            if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                IMetahash metahash;

                if (m_options.StoreMetadata)
                {
                    metahash = Utility.WrapMetadata(await MetadataGenerator.GenerateMetadataAsync(path, attributes, m_options, m_snapshot, m_logchannel), m_options);
                }
                else
                {
                    metahash = EMPTY_METADATA;
                }

                await m_logchannel.WriteAsync(LogMessage.Verbose("Adding directory {0}", path));
                AddFolderToOutput(path, lastwrite, metahash);
                return false;
            }

            // Regular file, keep going
            return true;
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private async Task AddFolderToOutput(string filename, DateTime lastModified, IMetahash meta)
        {
            long metadataid = -1;

            if (meta.Size > m_blocksize)
                throw new InvalidDataException(string.Format("Too large metadata, cannot handle more than {0} bytes", m_blocksize));

            await DataBlock.AddBlockToOutputAsync(m_blockoutput, meta.Hash, meta.Blob, 0, (int)meta.Size, CompressionHint.Default, false);
            await m_database.AddMetadatasetAsync(meta.Hash, meta.Size, ref metadataid);
            await m_database.AddDirectoryEntryAsync(filename, metadataid, lastModified);
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private async Task AddSymlinkToOutput(string filename, DateTime lastModified, IMetahash meta)
        {
            long metadataid = -1;

            if (meta.Size > m_blocksize)
                throw new InvalidDataException(string.Format("Too large metadata, cannot handle more than {0} bytes", m_blocksize));

            await DataBlock.AddBlockToOutputAsync(m_blockoutput, meta.Hash, meta.Blob, 0, (int)meta.Size, CompressionHint.Default, false);
            await m_database.AddMetadatasetAsync(meta.Hash, meta.Size, ref metadataid);
            await m_database.AddSymlinkEntryAsync(filename, metadataid, lastModified);
        }

        protected override async Task Start()
        {
            using(var input = m_input.AsReadOnly())
            {
                while (true)
                {
                    var path = await m_input.ReadAsync();

                    DateTime lastwrite = new DateTime(0, DateTimeKind.Utc);
                    FileAttributes attributes;
                    try 
                    { 
                        lastwrite = m_snapshot.GetLastWriteTimeUtc(path); 
                    }
                    catch (Exception ex) 
                    {
                        await m_logchannel.WriteAsync(LogMessage.Warning(string.Format("Failed to read timestamp on \"{0}\"", path), ex));
                    }

                    try 
                    { 
                        attributes = m_snapshot.GetAttributes(path); 
                    }
                    catch (Exception ex) 
                    {
                        await m_logchannel.WriteAsync(LogMessage.Warning(string.Format("Failed to read attributes on \"{0}\"", path), ex));
                    }

                    // If we only have metadata, stop here
                    if (await ProcessMetadata(path, attributes, lastwrite))
                    {
                        try
                        {
                            DateTime oldModified = new DateTime(0);
                            long lastFileSize = -1;
                            string oldMetahash = null;
                            long oldMetasize = -1;

                            var oldId = await m_database.GetFileEntryAsync(path, ref oldModified, ref lastFileSize, ref oldMetahash, ref oldMetasize);

                            await m_output.WriteAsync(new FileEntry() {
                                OldId = oldId,
                                Path = path,
                                Attributes = attributes,
                                LastWrite = lastwrite,
                                OldModified = oldModified,
                                LastFileSize = lastFileSize,
                                OldMetaHash = oldMetahash,
                                OldMetaSize = oldMetasize
                            });
                        }
                        catch(Exception ex)
                        {
                            await m_logchannel.WriteAsync(LogMessage.Error(string.Format("Failed to process entry, path: {0}", path), ex));
                        }
                    }
                }
            }
        }
    }
}

