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
using Duplicati.Library.Interface;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation.Backup
{
    public class FileSplitterProcess : ProcessHelper
    {
        public struct DataBlock
        {
            public string Hash;
            public byte[] Data;
            public long Offset;
            public long Size;
            public CompressionHint Hint;
            public bool IsBlocklistHashes;

            public static Task AddBlockToOutputAsync(IWriteChannel<DataBlock> channel, string key, byte[] data, long offset, long size, CompressionHint hint, bool isBlocklistHashes)
            {
                return m_output.WriteAsync(new DataBlock() {
                    Hash = key,
                    Data = data,
                    Offset = offset,
                    Size = size,
                    Hint = hint,
                    IsBlocklistHashes = isBlocklistHashes
                });
            }
        }

        [ChannelName("ProcessedFiles")]
        private IReadChannel<MetadataPreProcess.FileEntry> m_input;

        [ChannelName("LogChannel")]
        private IWriteChannel<LogMessage> m_logchannel;

        [ChannelName("OutputBlocks")]
        private IWriteChannel<DataBlock> m_output;

        private Options m_options;
        private Snapshots.ISnapshotService m_snapshot;
        private Database.LocalBackupDatabase m_database;

        private int m_blocksize;
        private System.Security.Cryptography.HashAlgorithm m_blockhasher;
        private System.Security.Cryptography.HashAlgorithm m_filehasher;
        private readonly IMetahash EMPTY_METADATA;

        public FileSplitterProcess(Snapshots.ISnapshotService snapshot, Options options, Database.LocalBackupDatabase database)
            : base()
        {
            m_snapshot = snapshot;
            m_options = options;
            m_database = database;

            m_blocksize = m_options.Blocksize;

            m_blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
            m_filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FileHashAlgorithm);

            EMPTY_METADATA = Utility.WrapMetadata(new Dictionary<string, string>(), options);
        }
            
        private async Task ProcessFile(MetadataPreProcess.FileEntry e)
        {

            m_result.OpenedFiles++;

            long filesize = 0;

            var hint = m_options.GetCompressionHintFromFilename(e.Path);
            string oldHash;
            lock(m_database.AccessLock)
                oldHash = e.OldId < 0 ? null : m_database.GetFileHash(e.OldId);

            var blockbuffer = new byte[m_options.Blocksize * Math.Max(1, m_options.FileReadBufferSize / m_options.Blocksize)];
            var blocklistbuffer = new byte[m_options.Blocksize];

            using (var blocklisthashes = new Library.Utility.FileBackedStringList())
            using (var hashcollector = new Library.Utility.FileBackedStringList())
            {
                using (var fs = new Blockprocessor(m_snapshot.OpenRead(e.Path), blockbuffer))
                {
                    try { m_result.OperationProgressUpdater.StartFile(e.Path, fs.Length); }
                    catch (Exception ex) { await m_logchannel.WriteAsync(LogMessage.Warning(string.Format("Failed to read file length for file {0}", e.Path), ex)); }

                    int blocklistoffset = 0;

                    m_filehasher.Initialize();

                    var offset = 0;
                    var remaining = fs.Readblock();

                    do
                    {
                        var size = Math.Min(m_blocksize, remaining);

                        m_filehasher.TransformBlock(blockbuffer, offset, size, blockbuffer, offset);
                        var blockkey = m_blockhasher.ComputeHash(blockbuffer, offset, size);
                        if (blocklistbuffer.Length - blocklistoffset < blockkey.Length)
                        {
                            var blkey = Convert.ToBase64String(m_blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));
                            blocklisthashes.Add(blkey);
                            await DataBlock.AddBlockToOutputAsync(m_output, blkey, blocklistbuffer, 0, blocklistoffset, CompressionHint.Noncompressible, true);
                            blocklistoffset = 0;
                            blocklistbuffer = new byte[m_options.Blocksize];
                        }

                        Array.Copy(blockkey, 0, blocklistbuffer, blocklistoffset, blockkey.Length);
                        blocklistoffset += blockkey.Length;

                        var key = Convert.ToBase64String(blockkey);
                        await DataBlock.AddBlockToOutputAsync(m_output, key, blockbuffer, offset, size, hint, false);
                        hashcollector.Add(key);
                        filesize += size;
                        blockbuffer = new byte[m_options.Blocksize * Math.Max(1, m_options.FileReadBufferSize / m_options.Blocksize)];

                        m_result.OperationProgressUpdater.UpdateFileProgress(filesize);
                        if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            return false;

                        remaining -= size;
                        offset += size;

                        if (remaining == 0)
                        {
                            offset = 0;
                            remaining = fs.Readblock();
                        }

                    } while (remaining > 0);

                    //If all fits in a single block, don't bother with blocklists
                    if (hashcollector.Count > 1)
                    {
                        var blkeyfinal = Convert.ToBase64String(m_blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));
                        blocklisthashes.Add(blkeyfinal);
                        await DataBlock.AddBlockToOutputAsync(m_output, blkeyfinal, blocklistbuffer, 0, blocklistoffset, CompressionHint.Noncompressible, true);
                    }
                }
            }
        }

        protected override async Task Start()
        {
            while (true)
            {
                var e = await m_input.ReadAsync();

                long filestatsize = -1;
                try { filestatsize = m_snapshot.GetFileSize(e.Path); }
                catch { }

                IMetahash metahashandsize = m_options.StoreMetadata ? Utility.WrapMetadata(MetadataGenerator.GenerateMetadataAsync(e.Path, e.Attributes, m_options, m_snapshot, m_logchannel), m_options) : EMPTY_METADATA;

                var timestampChanged = e.LastWrite != e.OldModified || e.LastWrite.Ticks == 0 || e.OldModified.Ticks == 0;
                var filesizeChanged = filestatsize < 0 || e.LastFileSize < 0 || filestatsize != e.LastFileSize;
                var tooLargeFile = m_options.SkipFilesLargerThan != long.MaxValue && m_options.SkipFilesLargerThan != 0 && filestatsize >= 0 && filestatsize > m_options.SkipFilesLargerThan;
                var metadatachanged = !m_options.SkipMetadata && (metahashandsize.Size != e.OldMetaSize || metahashandsize.Hash != e.OldMetaHash);

                if ((e.OldId < 0 || m_options.DisableFiletimeCheck || timestampChanged || filesizeChanged || metadatachanged) && !tooLargeFile)
                {
                    await m_logchannel.WriteAsync(LogMessage.Verbose("Checking file for changes {0}, new: {1}, timestamp changed: {2}, size changed: {3}, metadatachanged: {4}, {5} vs {6}", e.Path, e.OldId <= 0, timestampChanged, filesizeChanged, metadatachanged, e.LastWrite, e.OldModified));
                    await ProcessFile(e);
                }
                else
                {
                    if (m_options.SkipFilesLargerThan == long.MaxValue || m_options.SkipFilesLargerThan == 0 || m_snapshot.GetFileSize(e.Path) < m_options.SkipFilesLargerThan)                
                        await m_logchannel.WriteAsync(LogMessage.Verbose("Skipped checking file, because timestamp was not updated {0}", e.Path));
                    else
                        await m_logchannel.WriteAsync(LogMessage.Verbose("Skipped checking file, because the size exceeds limit {0}", e.Path));
                }
            }
        }
    }
}

