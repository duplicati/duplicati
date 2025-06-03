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

        public Task<long> FindBlockIDAsync(string hash, long size)
        {
            return RunOnMain(async () => await m_database.FindBlockID(hash, size));
        }

        public Task<bool> AddBlockAsync(string hash, long size, long volumeid)
        {
            return RunOnMain(async () => await m_database.AddBlock(hash, size, volumeid));
        }

        public Task<string> GetFileHashAsync(long fileid)
        {
            return RunOnMain(async () => await m_database.GetFileHash(fileid));
        }

        public Task<(bool, long)> AddMetadatasetAsync(string hash, long size, long blocksetid)
        {
            return RunOnMain(async () => await m_database.AddMetadataset(hash, size, blocksetid));
        }

        public Task<(bool, long)> GetMetadataIDAsync(string hash, long size)
        {
            return RunOnMain(async () => await m_database.GetMetadatasetID(hash, size));
        }

        public Task AddDirectoryEntryAsync(string filename, long metadataid, DateTime lastModified)
        {
            return RunOnMain(async () => await m_database.AddDirectoryEntry(filename, metadataid, lastModified));
        }

        public Task AddSymlinkEntryAsync(string filename, long metadataid, DateTime lastModified)
        {
            return RunOnMain(async () => await m_database.AddSymlinkEntry(filename, metadataid, lastModified));
        }

        public Task<(long Size, string MetadataHash)?> GetMetadataHashAndSizeForFileAsync(long fileid)
        {
            return RunOnMain(async () => await m_database.GetMetadataHashAndSizeForFile(fileid));
        }

        public Task<(long, DateTime, long)> GetFileLastModifiedAsync(long prefixid, string path, long lastfilesetid, bool includeLength)
        {
            return RunOnMain(async () => await m_database.GetFileLastModified(prefixid, path, lastfilesetid, includeLength));
        }

        public Task<FileEntryData> GetFileEntryAsync(long prefixid, string path, long lastfilesetid)
        {
            return RunOnMain(async () =>
            {
                var (id, oldModified, lastFileSize, oldMetahash, oldMetasize) =
                    await m_database.GetFileEntry(prefixid, path, lastfilesetid);
                return
                    id < 0 ?
                    null :
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

        public Task<long> AddBlocksetAsync(string filehash, long size, int blocksize, IEnumerable<string> hashlist, IEnumerable<string> blocklisthashes)
        {
            return RunOnMain(async () =>
            {
                var (_, blocksetid) = await m_database.AddBlockset(filehash, size, blocksize, hashlist, blocklisthashes);

                return blocksetid;
            });
        }

        public Task<long> GetOrCreatePathPrefix(string prefix)
        {
            return RunOnMain(async () => await m_database.GetOrCreatePathPrefix(prefix));
        }

        public Task AddFileAsync(long prefixid, string filename, DateTime lastmodified, long blocksetid, long metadataid)
        {
            return RunOnMain(async () => await m_database.AddFile(prefixid, filename, lastmodified, blocksetid, metadataid));
        }

        public Task AddUnmodifiedAsync(long fileid, DateTime lastModified)
        {
            return RunOnMain(async () => await m_database.AddKnownFile(fileid, lastModified));
        }


        public Task MoveBlockToVolumeAsync(string blockkey, long size, long sourcevolumeid, long targetvolumeid)
        {
            return RunOnMain(async () => await m_database.MoveBlockToVolume(blockkey, size, sourcevolumeid, targetvolumeid));
        }

        public Task SafeDeleteRemoteVolumeAsync(string remotename)
        {
            return RunOnMain(async () => await m_database.SafeDeleteRemoteVolume(remotename));
        }

        public Task<string[]> GetBlocklistHashesAsync(string remotename)
        {
            return RunOnMain(async () => await m_database.GetBlocklistHashes(remotename));
        }

        public Task UpdateIndexVolumeAsync(IndexVolumeWriter indexvolume, BlockVolumeWriter blockvolume)
        {
            if (indexvolume != null)
                return Task.FromResult<bool>(true);

            return RunOnMain(async () =>
            {
                await m_database.AddIndexBlockLink(indexvolume.VolumeID, blockvolume.VolumeID);
                indexvolume.StartVolume(blockvolume.RemoteFilename);

                await foreach (var b in m_database.GetBlocks(blockvolume.VolumeID))
                    indexvolume.AddBlock(b.Hash, b.Size);

                await m_database.UpdateRemoteVolume(indexvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
            });
        }

        public Task AppendFilesFromPreviousSetAsync()
        {
            return RunOnMain(async () => await m_database.AppendFilesFromPreviousSet());
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist)
        {
            return RunOnMain(async () => await m_database.AppendFilesFromPreviousSet(deletedFilelist));
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist, long filesetid, long prevId, DateTime timestamp)
        {
            return RunOnMain(async () => await m_database.AppendFilesFromPreviousSet(deletedFilelist, filesetid, prevId, timestamp));
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't
        /// yet part of the new fileset, and which aren't excluded by the (optional) exclusion
        /// predicate.
        /// </summary>
        /// <param name="exclusionPredicate">Optional exclusion predicate (true = exclude file)</param>
        public Task AppendFilesFromPreviousSetWithPredicateAsync(Func<string, long, bool> exclusionPredicate)
        {
            return RunOnMain(async () => await m_database.AppendFilesFromPreviousSetWithPredicate(exclusionPredicate));
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't
        /// yet part of the new fileset, and which aren't excluded by the (optional) exclusion
        /// predicate.
        /// </summary>
        /// <param name="exclusionPredicate">Optional exclusion predicate (true = exclude file)</param>
        /// <param name="fileSetId">Current fileset ID</param>
        /// <param name="prevFileSetId">Source fileset ID</param>
        /// <param name="timestamp">If <c>prevFileSetId</c> == -1, used to locate previous fileset</param>
        public Task AppendFilesFromPreviousSetWithPredicateAsync(Func<string, long, bool> exclusionPredicate, long fileSetId, long prevFileSetId, DateTime timestamp)
        {
            return RunOnMain(async () => await m_database.AppendFilesFromPreviousSetWithPredicate(exclusionPredicate, fileSetId, prevFileSetId, timestamp));
        }

        public Task<KeyValuePair<long, DateTime>[]> GetIncompleteFilesetsAsync()
        {
            return RunOnMain(async () => await m_database.GetIncompleteFilesets().OrderBy(x => x.Value).ToArrayAsync());
        }

        public Task<KeyValuePair<long, DateTime>[]> GetFilesetTimesAsync()
        {
            return RunOnMain(async () => await m_database.FilesetTimes().ToArrayAsync());
        }

        public Task<long> CreateFilesetAsync(long volumeID, DateTime fileTime)
        {
            return RunOnMain(async () =>
                {
                    var fs = await m_database.CreateFileset(volumeID, fileTime);
                    await m_database.Transaction.CommitAsync();
                    return fs;
                });
        }

        public Task LinkFilesetToVolumeAsync(long filesetid, long volumeid)
        {
            return RunOnMain(async () => await m_database.LinkFilesetToVolume(filesetid, volumeid));
        }

        public Task WriteFilesetAsync(FilesetVolumeWriter fsw, long filesetid)
        {
            return RunOnMain(async () => await m_database.WriteFileset(fsw, filesetid));
        }

        public Task PushTimestampChangesToPreviousVersionAsync(long filesetid)
        {
            return RunOnMain(async () => await m_database.PushTimestampChangesToPreviousVersion(filesetid));
        }

        public Task UpdateFilesetAndMarkAsFullBackupAsync(long filesetid)
        {
            return RunOnMain(async () => await m_database.UpdateFullBackupStateInFileset(filesetid, true));
        }

        // TODO: Make IAsyncEnumerable or get rid of the SingleRunner setup
        public Task<string[]> GetMissingIndexFilesAsync()
        {
            return RunOnMain(async () => await m_database.GetMissingIndexFiles().ToArrayAsync());
        }

        public Task UpdateChangeStatisticsAsync(BackupResults result)
        {
            return RunOnMain(async () => await m_database.UpdateChangeStatistics(result));
        }

        public Task VerifyConsistencyAsync(int blocksize, int blockhashSize, bool verifyfilelists)
        {
            return RunOnMain(async () => await m_database.VerifyConsistency(blocksize, blockhashSize, verifyfilelists));
        }

        public Task RemoveRemoteVolumeAsync(string remoteFilename)
        {
            return RunOnMain(async () => await m_database.RemoveRemoteVolume(remoteFilename));
        }

        public Task<RemoteVolumeEntry> GetRemoteVolumeFromFilesetIDAsync(long filesetID)
        {
            return RunOnMain(async () => await m_database.GetRemoteVolumeFromFilesetID(filesetID));
        }

        public Task CreateChangeJournalDataAsync(IEnumerable<USNJournalDataEntry> journalData)
        {
            return RunOnMain(async () => await m_database.CreateChangeJournalData(journalData));
        }

        public Task UpdateChangeJournalDataAsync(IEnumerable<USNJournalDataEntry> journalData, long lastfilesetid)
        {
            return RunOnMain(async () => await m_database.UpdateChangeJournalData(journalData, lastfilesetid));
        }

        public Task<bool> IsBlocklistHashKnownAsync(string hash)
        {
            return RunOnMain(async () => await m_database.IsBlocklistHashKnown(hash));
        }

    }
}

