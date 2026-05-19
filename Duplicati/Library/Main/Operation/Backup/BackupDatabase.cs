// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;
using CoCoL;
using Duplicati.Library.Main.Operation.Common;
using System.Collections.Generic;
using Duplicati.Library.Main.Volumes;
using System.Linq;
using Duplicati.Library.Interface;
using System.Threading;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// Asynchronous interface that ensures all requests
    /// to the database are performed in a sequential manner
    /// </summary>
    internal class BackupDatabase : DatabaseCommon
    {
        private readonly LocalBackupDatabase m_database;

        public class FileEntryData
        {
            public long id;
            public DateTime modified;
            public long filesize;
            public string metahash;
            public long metasize;
        }

        public BackupDatabase(LocalBackupDatabase database, Options options)
            : base(database, options)
        {
            m_database = database;
        }

        public Task<long> FindBlockIDAsync(string hash, long size, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .FindBlockIDAsync(hash, size, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<bool> AddBlockAsync(string hash, long size, long volumeid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .AddBlockAsync(hash, size, volumeid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<string> GetFileHashAsync(long fileid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .GetFileHashAsync(fileid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<(bool, long)> AddMetadatasetAsync(string hash, long size, long blocksetid, IMetahash metahash, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .AddMetadatasetAsync(hash, size, blocksetid, metahash, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<(bool, long)> GetMetadataIDAsync(string hash, long size, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .GetMetadatasetIDAsync(hash, size, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AddDirectoryEntryAsync(string filename, long metadataid, DateTime lastModified, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .AddDirectoryEntryAsync(filename, metadataid, lastModified, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AddSymlinkEntryAsync(string filename, long metadataid, DateTime lastModified, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .AddSymlinkEntryAsync(filename, metadataid, lastModified, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<(string MetadataHash, long Size)?> GetMetadataHashAndSizeForFileAsync(long fileid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .GetMetadataHashAndSizeForFileAsync(fileid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<(long, DateTime, long)> GetFileLastModifiedAsync(long prefixid, string path, long lastfilesetid, bool includeLength, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .GetFileLastModifiedAsync(prefixid, path, lastfilesetid, includeLength, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<FileEntryData> GetFileEntryAsync(long prefixid, string path, long lastfilesetid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
            {
                var (id, oldModified, lastFileSize, oldMetahash, oldMetasize) =
                    await m_database.GetFileEntryAsync(prefixid, path, lastfilesetid, cancellationToken)
                        .ConfigureAwait(false);

                return
                    id < 0 ?
                    null
                    :
                    new FileEntryData()
                    {
                        id = id,
                        modified = oldModified,
                        filesize = lastFileSize,
                        metahash = oldMetahash,
                        metasize = oldMetasize
                    };
            });
        }

        public Task<long> AddBlocksetAsync(string filehash, long size, int blocksize, IEnumerable<string> hashlist, IEnumerable<string> blocklisthashes, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
            {
                var (_, blocksetid) = await m_database
                    .AddBlocksetAsync(filehash, size, blocksize, hashlist, blocklisthashes, cancellationToken)
                    .ConfigureAwait(false);

                return blocksetid;
            });
        }

        public Task<long> GetOrCreatePathPrefixAsync(string prefix, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .GetOrCreatePathPrefixAsync(prefix, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AddFileAsync(long prefixid, string filename, DateTime lastmodified, long blocksetid, long metadataid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .AddFileAsync(prefixid, filename, lastmodified, blocksetid, metadataid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AddUnmodifiedAsync(long fileid, DateTime lastModified, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .AddKnownFileAsync(fileid, lastModified, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task RemoveDuplicatePathsFromFilesetAsync(long filesetId, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .RemoveDuplicatePathsFromFilesetAsync(filesetId, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task MoveBlockToVolumeAsync(string blockkey, long size, long sourcevolumeid, long targetvolumeid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .MoveBlockToVolumeAsync(blockkey, size, sourcevolumeid, targetvolumeid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task SafeDeleteRemoteVolumeAsync(string remotename, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .SafeDeleteRemoteVolumeAsync(remotename, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<string[]> GetBlocklistHashesAsync(string remotename, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .GetBlocklistHashesAsync(remotename, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task UpdateIndexVolumeAsync(IndexVolumeWriter indexvolume, BlockVolumeWriter blockvolume, CancellationToken cancellationToken)
        {
            if (indexvolume != null)
                return Task.FromResult<bool>(true);

            return RunOnMainAsync(async () =>
            {
                await m_database
                    .AddIndexBlockLinkAsync(indexvolume.VolumeID, blockvolume.VolumeID, cancellationToken)
                    .ConfigureAwait(false);

                indexvolume.StartVolume(blockvolume.RemoteFilename);

                await foreach (var b in m_database.GetBlocksAsync(blockvolume.VolumeID, cancellationToken).ConfigureAwait(false))
                    indexvolume.AddBlock(b.Hash, b.Size);

                await m_database
                    .UpdateRemoteVolumeAsync(indexvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, cancellationToken)
                    .ConfigureAwait(false);
            });
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .AppendFilesFromPreviousSetAsync(deletedFilelist, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist, long filesetid, long prevId, DateTime timestamp, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .AppendFilesFromPreviousSetAsync(deletedFilelist, filesetid, prevId, timestamp, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't
        /// yet part of the new fileset, and which aren't excluded by the (optional) exclusion
        /// predicate.
        /// </summary>
        /// <param name="exclusionPredicate">Optional exclusion predicate (true = exclude file)</param>
        public Task AppendFilesFromPreviousSetWithPredicateAsync(Func<string, long, bool> exclusionPredicate, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .AppendFilesFromPreviousSetWithPredicateAsync(exclusionPredicate, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<KeyValuePair<long, DateTime>[]> GetIncompleteFilesetsAsync(CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .GetIncompleteFilesetsAsync(cancellationToken)
                    .OrderBy(x => x.Value)
                    .ToArrayAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<KeyValuePair<long, DateTime>[]> GetFilesetTimesAsync(CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .FilesetTimesAsync(cancellationToken)
                    .ToArrayAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<long> CreateFilesetAsync(long volumeID, DateTime fileTime, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                {
                    var fs = await m_database
                        .CreateFilesetAsync(volumeID, fileTime, cancellationToken)
                        .ConfigureAwait(false);

                    await m_database.Transaction
                        .CommitAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return fs;
                });
        }

        public Task LinkFilesetToVolumeAsync(long filesetid, long volumeid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .LinkFilesetToVolumeAsync(filesetid, volumeid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task WriteFilesetAsync(FilesetVolumeWriter fsw, long filesetid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .WriteFilesetAsync(fsw, filesetid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task PushTimestampChangesToPreviousVersionAsync(long filesetid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .PushTimestampChangesToPreviousVersionAsync(filesetid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task UpdateFilesetAndMarkAsFullBackupAsync(long filesetid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .UpdateFullBackupStateInFilesetAsync(filesetid, true, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<IAsyncEnumerable<string>> GetMissingIndexFilesAsync(CancellationToken cancellationToken)
        {
            return RunOnMainAsync(() => m_database.GetMissingIndexFilesAsync(cancellationToken));
        }

        public Task UpdateChangeStatisticsAsync(BackupResults result, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .UpdateChangeStatisticsAsync(result, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task VerifyConsistencyAsync(int blocksize, int blockhashSize, bool verifyfilelists, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .VerifyConsistencyAsync(blocksize, blockhashSize, verifyfilelists, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task RemoveRemoteVolumeAsync(string remoteFilename, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .RemoveRemoteVolumeAsync(remoteFilename, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<RemoteVolumeEntry> GetRemoteVolumeFromFilesetIDAsync(long filesetID, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .GetRemoteVolumeFromFilesetIDAsync(filesetID, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task CreateChangeJournalDataAsync(IEnumerable<USNJournalDataEntry> journalData, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .CreateChangeJournalDataAsync(journalData, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task UpdateChangeJournalDataAsync(IEnumerable<USNJournalDataEntry> journalData, long lastfilesetid, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .UpdateChangeJournalDataAsync(journalData, lastfilesetid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<bool> IsBlocklistHashKnownAsync(string hash, CancellationToken cancellationToken)
        {
            return RunOnMainAsync(async () =>
                await m_database
                    .IsBlocklistHashKnownAsync(hash, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

    }
}

