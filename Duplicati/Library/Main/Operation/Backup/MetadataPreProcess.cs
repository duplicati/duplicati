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
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This class processes paths for metadata and emits the metadata blocks for storage.
    /// Folders and symlinks in the database, and paths are forwarded to be scanned for changes
    /// </summary>
    internal static class MetadataPreProcess
    {
        private static readonly string FILELOGTAG = Logging.Log.LogTagFromType(typeof(MetadataPreProcess)) + ".FileEntry";

        public class FileEntry
        {
            // From input
            public string Path;

            // From database
            public long OldId;
            public DateTime OldModified;
            public long LastFileSize;
            public string OldMetaHash;
            public long OldMetaSize;

            // From filedata
            public DateTime LastWrite;
            public FileAttributes Attributes;

            // After processing metadata
            public IMetahash MetaHashAndSize;
            public bool MetadataChanged;
        }
            
        public static Task Run(Snapshots.ISnapshotService snapshot, Options options, BackupDatabase database, long lastfilesetid)
        {
            return AutomationExtensions.RunTask(new
            {
                Input = Backup.Channels.SourcePaths.ForRead,
                StreamBlockChannel = Channels.StreamBlock.ForWrite,
                Output = Backup.Channels.ProcessedFiles.ForWrite,
            },
                
            async self =>
            {
                var emptymetadata = Utility.WrapMetadata(new Dictionary<string, string>(), options);

                while (true)
                {
                    var path = await self.Input.ReadAsync();

                    var lastwrite = new DateTime(0, DateTimeKind.Utc);
                    var attributes = default(FileAttributes);
                    try 
                    { 
                        lastwrite = snapshot.GetLastWriteTimeUtc(path); 
                    }
                    catch (Exception ex) 
                    {
                        Logging.Log.WriteWarningMessage(FILELOGTAG, "TimestampReadFailed", ex, "Failed to read timestamp on \"{0}\"", path);
                    }

                    try 
                    { 
                        attributes = snapshot.GetAttributes(path); 
                    }
                    catch (Exception ex) 
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "FailedAttributeRead", "Failed to read attributes from {0}: {1}", path, ex.Message);                     
                    }

                    // If we only have metadata, stop here
                    if (await ProcessMetadata(path, attributes, lastwrite, options, snapshot, emptymetadata, database, self.StreamBlockChannel))
                    {
                        try
                        {
                            if (options.CheckFiletimeOnly || options.DisableFiletimeCheck)
                            {
                                var tmp = await database.GetFileLastModifiedAsync(path, lastfilesetid);
                                await self.Output.WriteAsync(new FileEntry() {
                                    OldId = tmp.Key < 0 ? -1 : tmp.Key,
                                    Path = path,
                                    Attributes = attributes,
                                    LastWrite = lastwrite,
                                    OldModified = tmp.Key < 0 ? new DateTime(0) : tmp.Value,
                                    LastFileSize = -1 ,
                                    OldMetaHash = null,
                                    OldMetaSize = -1
                                });
                            }
                            else
                            {
                                var res = await database.GetFileEntryAsync(path, lastfilesetid);
                                await self.Output.WriteAsync(new FileEntry() {
                                    OldId = res == null ? -1 : res.id,
                                    Path = path,
                                    Attributes = attributes,
                                    LastWrite = lastwrite,
                                    OldModified = res == null ? new DateTime(0) : res.modified,
                                    LastFileSize = res == null ? -1 : res.filesize,
                                    OldMetaHash = res == null ? null : res.metahash,
                                    OldMetaSize = res == null ? -1 : res.metasize
                                });
                            }
                        }
                        catch(Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(FILELOGTAG, "ProcessingMetadataFailed", ex, "Failed to process entry, path: {0}", path);
                        }
                    }
                }

            });

        }

        /// <summary>
        /// Processes the metadata for the given path.
        /// </summary>
        /// <returns><c>True</c> if the path should be submitted to more analysis, <c>false</c> if there is nothing else to do</returns>
        private static async Task<bool> ProcessMetadata(string path, FileAttributes attributes, DateTime lastwrite, Options options, Snapshots.ISnapshotService snapshot, IMetahash emptymetadata, BackupDatabase database, IWriteChannel<StreamBlock> streamblockchannel)
        {
            if (snapshot.IsSymlink(path, attributes))
            {
                // Not all reparse points are symlinks.
                // For example, on Windows 10 Fall Creator's Update, the OneDrive folder (and all subfolders)
                // are reparse points, which allows the folder to hook into the OneDrive service and download things on-demand.
                // If we can't find a symlink target for the current path, we won't treat it as a symlink.                
                string symlinkTarget = snapshot.GetSymlinkTarget(path);
                if (!string.IsNullOrWhiteSpace(symlinkTarget))
                {
                    if (options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "IgnoreSymlink", "Ignoring symlink {0}", path);
                        return false;
                    }

                    if (options.SymlinkPolicy == Options.SymlinkStrategy.Store)
                    {
                        var metadata = await MetadataGenerator.GenerateMetadataAsync(path, attributes, options, snapshot);

                        if (!metadata.ContainsKey("CoreSymlinkTarget"))
                            metadata["CoreSymlinkTarget"] = symlinkTarget;

                        var metahash = Utility.WrapMetadata(metadata, options);
                        await AddSymlinkToOutputAsync(path, DateTime.UtcNow, metahash, database, streamblockchannel);

                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "StoreSymlink", "Stored symlink {0}", path);
                        // Don't process further
                        return false;
                    }
                }
                else
                {
                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "FollowingEmptySymlink", "Treating empty symlink as regular path {0}", path);
                }
            }

            if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                IMetahash metahash;

                if (options.StoreMetadata)
                {
                    metahash = Utility.WrapMetadata(await MetadataGenerator.GenerateMetadataAsync(path, attributes, options, snapshot), options);
                }
                else
                {
                    metahash = emptymetadata;
                }

                Logging.Log.WriteVerboseMessage(FILELOGTAG, "AddDirectory", "Adding directory {0}", path);
                await AddFolderToOutputAsync(path, lastwrite, metahash, database, streamblockchannel);
                return false;
            }

            // Regular file, keep going
            return true;
        }

        /// <summary>
        /// Adds metadata to output, and returns the metadataset ID
        /// </summary>
        /// <returns>The metadataset ID.</returns>
        /// <param name="path">The path for which metadata is processed.</param>
        /// <param name="meta">The metadata entry.</param>
        /// <param name="database">The database connection.</param>
        /// <param name="streamblockchannel">The channel to write streams to.</param>
        internal static async Task<Tuple<bool, long>> AddMetadataToOutputAsync(string path, IMetahash meta, BackupDatabase database, IWriteChannel<StreamBlock> streamblockchannel)
        {
            StreamProcessResult res;
            using (var ms = new MemoryStream(meta.Blob))
                res = await StreamBlock.ProcessStream(streamblockchannel, path, ms, true, CompressionHint.Default); 

            return await database.AddMetadatasetAsync(res.Streamhash, res.Streamlength, res.Blocksetid);
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        private static async Task AddFolderToOutputAsync(string filename, DateTime lastModified, IMetahash meta, BackupDatabase database, IWriteChannel<StreamBlock> streamblockchannel)
        {
            var metadataid = await AddMetadataToOutputAsync(filename, meta, database, streamblockchannel);
            await database.AddDirectoryEntryAsync(filename, metadataid.Item2, lastModified);
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="database">The database to use</param>
        /// <param name="streamblockchannel">The channel to write blocks to</param>
        /// <param name="meta">The metadata ti record</param>
        private static async Task AddSymlinkToOutputAsync(string filename, DateTime lastModified, IMetahash meta, BackupDatabase database, IWriteChannel<StreamBlock> streamblockchannel)
        {
            var metadataid = await AddMetadataToOutputAsync(filename, meta, database, streamblockchannel);
            await database.AddSymlinkEntryAsync(filename, metadataid.Item2, lastModified);
        }

    }
}

