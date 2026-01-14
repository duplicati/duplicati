// Copyright (C) 2025, The Duplicati Team
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
            return RunOnMain(async () =>
                await m_database
                    .FindBlockID(hash, size, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<bool> AddBlockAsync(string hash, long size, long volumeid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AddBlock(hash, size, volumeid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<string> GetFileHashAsync(long fileid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .GetFileHash(fileid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<(bool, long)> AddMetadatasetAsync(string hash, long size, long blocksetid, IMetahash metahash, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AddMetadataset(hash, size, blocksetid, metahash, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<(bool, long)> GetMetadataIDAsync(string hash, long size, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .GetMetadatasetID(hash, size, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AddDirectoryEntryAsync(string filename, long metadataid, DateTime lastModified, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AddDirectoryEntry(filename, metadataid, lastModified, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AddSymlinkEntryAsync(string filename, long metadataid, DateTime lastModified, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AddSymlinkEntry(filename, metadataid, lastModified, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<(string MetadataHash, long Size)?> GetMetadataHashAndSizeForFileAsync(long fileid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .GetMetadataHashAndSizeForFile(fileid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<(long, DateTime, long)> GetFileLastModifiedAsync(long prefixid, string path, long lastfilesetid, bool includeLength, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .GetFileLastModified(prefixid, path, lastfilesetid, includeLength, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<FileEntryData> GetFileEntryAsync(long prefixid, string path, long lastfilesetid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
            {
                var (id, oldModified, lastFileSize, oldMetahash, oldMetasize) =
                    await m_database.GetFileEntry(prefixid, path, lastfilesetid, cancellationToken)
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
            return RunOnMain(async () =>
            {
                var (_, blocksetid) = await m_database
                    .AddBlockset(filehash, size, blocksize, hashlist, blocklisthashes, cancellationToken)
                    .ConfigureAwait(false);

                return blocksetid;
            });
        }

        public Task<long> GetOrCreatePathPrefix(string prefix, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .GetOrCreatePathPrefix(prefix, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AddFileAsync(long prefixid, string filename, DateTime lastmodified, long blocksetid, long metadataid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AddFile(prefixid, filename, lastmodified, blocksetid, metadataid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AddUnmodifiedAsync(long fileid, DateTime lastModified, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AddKnownFile(fileid, lastModified, cancellationToken)
                    .ConfigureAwait(false)
            );
        }


        public Task MoveBlockToVolumeAsync(string blockkey, long size, long sourcevolumeid, long targetvolumeid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .MoveBlockToVolume(blockkey, size, sourcevolumeid, targetvolumeid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task SafeDeleteRemoteVolumeAsync(string remotename, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .SafeDeleteRemoteVolume(remotename, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<string[]> GetBlocklistHashesAsync(string remotename, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .GetBlocklistHashes(remotename, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task UpdateIndexVolumeAsync(IndexVolumeWriter indexvolume, BlockVolumeWriter blockvolume, CancellationToken cancellationToken)
        {
            if (indexvolume != null)
                return Task.FromResult<bool>(true);

            return RunOnMain(async () =>
            {
                await m_database
                    .AddIndexBlockLink(indexvolume.VolumeID, blockvolume.VolumeID, cancellationToken)
                    .ConfigureAwait(false);

                indexvolume.StartVolume(blockvolume.RemoteFilename);

                await foreach (var b in m_database.GetBlocks(blockvolume.VolumeID, cancellationToken).ConfigureAwait(false))
                    indexvolume.AddBlock(b.Hash, b.Size);

                await m_database
                    .UpdateRemoteVolume(indexvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, cancellationToken)
                    .ConfigureAwait(false);
            });
        }

        public Task AppendFilesFromPreviousSetAsync(CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AppendFilesFromPreviousSet(null, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AppendFilesFromPreviousSet(deletedFilelist, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist, long filesetid, long prevId, DateTime timestamp, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AppendFilesFromPreviousSet(deletedFilelist, filesetid, prevId, timestamp, cancellationToken)
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
            return RunOnMain(async () =>
                await m_database
                    .AppendFilesFromPreviousSetWithPredicate(exclusionPredicate, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't
        /// yet part of the new fileset, and which aren't excluded by the (optional) exclusion
        /// predicate.
        /// </summary>
        /// <param name="exclusionPredicate">Optional exclusion predicate (true = exclude file).</param>
        /// <param name="fileSetId">Current fileset ID.</param>
        /// <param name="prevFileSetId">Source fileset ID.</param>
        /// <param name="timestamp">If <c>prevFileSetId</c> == -1, used to locate previous fileset.</param>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <returns>Task that completes when the files have been appended.</returns>
        public Task AppendFilesFromPreviousSetWithPredicateAsync(Func<string, long, bool> exclusionPredicate, long fileSetId, long prevFileSetId, DateTime timestamp, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .AppendFilesFromPreviousSetWithPredicate(exclusionPredicate, fileSetId, prevFileSetId, timestamp, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<KeyValuePair<long, DateTime>[]> GetIncompleteFilesetsAsync(CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .GetIncompleteFilesets(cancellationToken)
                    .OrderBy(x => x.Value)
                    .ToArrayAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<KeyValuePair<long, DateTime>[]> GetFilesetTimesAsync(CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .FilesetTimes(cancellationToken)
                    .ToArrayAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<long> CreateFilesetAsync(long volumeID, DateTime fileTime, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                {
                    var fs = await m_database
                        .CreateFileset(volumeID, fileTime, cancellationToken)
                        .ConfigureAwait(false);

                    await m_database.Transaction
                        .CommitAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return fs;
                });
        }

        public Task LinkFilesetToVolumeAsync(long filesetid, long volumeid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .LinkFilesetToVolume(filesetid, volumeid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task WriteFilesetAsync(FilesetVolumeWriter fsw, long filesetid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .WriteFileset(fsw, filesetid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task PushTimestampChangesToPreviousVersionAsync(long filesetid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .PushTimestampChangesToPreviousVersion(filesetid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task UpdateFilesetAndMarkAsFullBackupAsync(long filesetid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .UpdateFullBackupStateInFileset(filesetid, true, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<IAsyncEnumerable<string>> GetMissingIndexFilesAsync(CancellationToken cancellationToken)
        {
            return RunOnMain(() => m_database.GetMissingIndexFiles(cancellationToken));
        }

        public Task UpdateChangeStatisticsAsync(BackupResults result, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .UpdateChangeStatistics(result, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task VerifyConsistencyAsync(int blocksize, int blockhashSize, bool verifyfilelists, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .VerifyConsistency(blocksize, blockhashSize, verifyfilelists, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task RemoveRemoteVolumeAsync(string remoteFilename, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .RemoveRemoteVolume(remoteFilename, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<RemoteVolumeEntry> GetRemoteVolumeFromFilesetIDAsync(long filesetID, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .GetRemoteVolumeFromFilesetID(filesetID, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task CreateChangeJournalDataAsync(IEnumerable<USNJournalDataEntry> journalData, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .CreateChangeJournalData(journalData, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task UpdateChangeJournalDataAsync(IEnumerable<USNJournalDataEntry> journalData, long lastfilesetid, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .UpdateChangeJournalData(journalData, lastfilesetid, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<bool> IsBlocklistHashKnownAsync(string hash, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_database
                    .IsBlocklistHashKnown(hash, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

    }
}

