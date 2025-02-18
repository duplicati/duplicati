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
            return RunOnMain(() => m_database.FindBlockID(hash, size, GetTransaction()));
        }

        public Task<bool> AddBlockAsync(string hash, long size, long volumeid)
        {
            return RunOnMain(() => m_database.AddBlock(hash, size, volumeid, GetTransaction()));
        }

        public Task<string> GetFileHashAsync(long fileid)
        {
            return RunOnMain(() => m_database.GetFileHash(fileid, GetTransaction()));
        }

        public Task<Tuple<bool, long>> AddMetadatasetAsync(string hash, long size, long blocksetid)
        {
            return RunOnMain(() =>
            {
                long metadataid;
                var n = m_database.AddMetadataset(hash, size, blocksetid, out metadataid, GetTransaction());
                return new Tuple<bool, long>(n, metadataid);
            });
        }

        public Task<Tuple<bool, long>> GetMetadataIDAsync(string hash, long size)
        {
            return RunOnMain(() =>
            {
                long metadataid;
                var r = m_database.GetMetadatasetID(hash, size, out metadataid, GetTransaction());
                return new Tuple<bool, long>(r, metadataid);
            });
        }

        public Task AddDirectoryEntryAsync(string filename, long metadataid, DateTime lastModified)
        {
            return RunOnMain(() => m_database.AddDirectoryEntry(filename, metadataid, lastModified, GetTransaction()));
        }

        public Task AddSymlinkEntryAsync(string filename, long metadataid, DateTime lastModified)
        {
            return RunOnMain(() => m_database.AddSymlinkEntry(filename, metadataid, lastModified, GetTransaction()));
        }

        public Task<Tuple<long, string>> GetMetadataHashAndSizeForFileAsync(long fileid)
        {
            return RunOnMain(() => m_database.GetMetadataHashAndSizeForFile(fileid, GetTransaction()));
        }

        public Task<Tuple<long, DateTime, long>> GetFileLastModifiedAsync(long prefixid, string path, long lastfilesetid, bool includeLength)
        {
            return RunOnMain(() =>
            {
                var id = m_database.GetFileLastModified(prefixid, path, lastfilesetid, includeLength, out var lastModified, out var length, GetTransaction());
                return new Tuple<long, DateTime, long>(id, lastModified, length);
            });
        }

        public Task<FileEntryData> GetFileEntryAsync(long prefixid, string path, long lastfilesetid)
        {
            return RunOnMain(() =>
            {
                DateTime oldModified;
                long lastFileSize;
                string oldMetahash;
                long oldMetasize;

                var id = m_database.GetFileEntry(prefixid, path, lastfilesetid, out oldModified, out lastFileSize, out oldMetahash, out oldMetasize, GetTransaction());
                return
                    id < 0 ?
                    null :
                    new FileEntryData() { id = id, modified = oldModified, filesize = lastFileSize, metahash = oldMetahash, metasize = oldMetasize };
            });
        }

        public Task<long> AddBlocksetAsync(string filehash, long size, int blocksize, IEnumerable<string> hashlist, IEnumerable<string> blocklisthashes)
        {
            return RunOnMain(() =>
            {
                long blocksetid;

                m_database.AddBlockset(filehash, size, blocksize, hashlist, blocklisthashes, out blocksetid, GetTransaction());
                return blocksetid;
            });
        }

        public Task<long> GetOrCreatePathPrefix(string prefix)
        {
            return RunOnMain(() => m_database.GetOrCreatePathPrefix(prefix, GetTransaction()));
        }

        public Task AddFileAsync(long prefixid, string filename, DateTime lastmodified, long blocksetid, long metadataid)
        {
            return RunOnMain(() => m_database.AddFile(prefixid, filename, lastmodified, blocksetid, metadataid, GetTransaction()));
        }

        public Task AddUnmodifiedAsync(long fileid, DateTime lastModified)
        {
            return RunOnMain(() => m_database.AddUnmodifiedFile(fileid, lastModified, GetTransaction()));
        }


        public Task MoveBlockToVolumeAsync(string blockkey, long size, long sourcevolumeid, long targetvolumeid)
        {
            return RunOnMain(() => m_database.MoveBlockToVolume(blockkey, size, sourcevolumeid, targetvolumeid, GetTransaction()));
        }

        public Task SafeDeleteRemoteVolumeAsync(string remotename)
        {
            return RunOnMain(() => m_database.SafeDeleteRemoteVolume(remotename, GetTransaction()));
        }

        public Task<string[]> GetBlocklistHashesAsync(string remotename)
        {
            return RunOnMain(() => Task.FromResult(m_database.GetBlocklistHashes(remotename, GetTransaction())));
        }

        public Task UpdateIndexVolumeAsync(IndexVolumeWriter indexvolume, BlockVolumeWriter blockvolume)
        {
            if (indexvolume != null)
                return Task.FromResult<bool>(true);

            return RunOnMain(() =>
            {
                m_database.AddIndexBlockLink(indexvolume.VolumeID, blockvolume.VolumeID, GetTransaction());
                indexvolume.StartVolume(blockvolume.RemoteFilename);

                foreach (var b in m_database.GetBlocks(blockvolume.VolumeID, GetTransaction()))
                    indexvolume.AddBlock(b.Hash, b.Size);

                m_database.UpdateRemoteVolume(indexvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, GetTransaction());
            });
        }

        public Task AppendFilesFromPreviousSetAsync()
        {
            return RunOnMain(() => m_database.AppendFilesFromPreviousSet(GetTransaction()));
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist)
        {
            return RunOnMain(() => m_database.AppendFilesFromPreviousSet(GetTransaction(), deletedFilelist));
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist, long filesetid, long prevId, DateTime timestamp)
        {
            return RunOnMain(() => m_database.AppendFilesFromPreviousSet(GetTransaction(), deletedFilelist, filesetid, prevId, timestamp));
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't 
        /// yet part of the new fileset, and which aren't excluded by the (optional) exclusion 
        /// predicate.
        /// </summary>
        /// <param name="exclusionPredicate">Optional exclusion predicate (true = exclude file)</param>
        public Task AppendFilesFromPreviousSetWithPredicateAsync(Func<string, long, bool> exclusionPredicate)
        {
            return RunOnMain(() => m_database.AppendFilesFromPreviousSetWithPredicate(GetTransaction(), exclusionPredicate));
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
            return RunOnMain(() => m_database.AppendFilesFromPreviousSetWithPredicate(GetTransaction(), exclusionPredicate, fileSetId, prevFileSetId, timestamp));
        }

        public Task<KeyValuePair<long, DateTime>[]> GetIncompleteFilesetsAsync()
        {
            return RunOnMain(() => m_database.GetIncompleteFilesets(GetTransaction()).OrderBy(x => x.Value).ToArray());
        }

        public Task<KeyValuePair<long, DateTime>[]> GetFilesetTimesAsync()
        {
            return RunOnMain(() => m_database.FilesetTimes.ToArray());
        }

        public Task<long> CreateFilesetAsync(long volumeID, DateTime fileTime)
        {
            return RunOnMain(() => m_database.CreateFileset(volumeID, fileTime, GetTransaction()));
        }

        public Task LinkFilesetToVolumeAsync(long filesetid, long volumeid)
        {
            return RunOnMain(() => m_database.LinkFilesetToVolume(filesetid, volumeid, GetTransaction()));
        }

        public Task WriteFilesetAsync(FilesetVolumeWriter fsw, long filesetid)
        {
            return RunOnMain(() => m_database.WriteFileset(fsw, filesetid, GetTransaction()));
        }

        public Task PushTimestampChangesToPreviousVersionAsync(long filesetid)
        {
            return RunOnMain(() => m_database.PushTimestampChangesToPreviousVersion(filesetid, GetTransaction()));
        }

        public Task UpdateFilesetAndMarkAsFullBackupAsync(long filesetid)
        {
            return RunOnMain(() => m_database.UpdateFullBackupStateInFileset(filesetid, true, GetTransaction()));
        }

        // TODO: Make IAsyncEnumerable or get rid of the SingleRunner setup
        public Task<string[]> GetMissingIndexFilesAsync()
        {
            return RunOnMain(() => m_database.GetMissingIndexFiles(GetTransaction()).ToArray());
        }

        public Task UpdateChangeStatisticsAsync(BackupResults result)
        {
            return RunOnMain(() => m_database.UpdateChangeStatistics(result, GetTransaction()));
        }

        public Task VerifyConsistencyAsync(int blocksize, int blockhashSize, bool verifyfilelists)
        {
            return RunOnMain(() => m_database.VerifyConsistency(blocksize, blockhashSize, verifyfilelists, GetTransaction()));
        }

        public Task RemoveRemoteVolumeAsync(string remoteFilename)
        {
            return RunOnMain(() => m_database.RemoveRemoteVolume(remoteFilename, GetTransaction()));
        }

        public Task<RemoteVolumeEntry> GetRemoteVolumeFromFilesetIDAsync(long filesetID)
        {
            return RunOnMain(() => m_database.GetRemoteVolumeFromFilesetID(filesetID, GetTransaction()));
        }

        public Task CreateChangeJournalDataAsync(IEnumerable<USNJournalDataEntry> journalData)
        {
            return RunOnMain(() => m_database.CreateChangeJournalData(journalData, GetTransaction()));
        }

        public Task UpdateChangeJournalDataAsync(IEnumerable<USNJournalDataEntry> journalData, long lastfilesetid)
        {
            return RunOnMain(() => m_database.UpdateChangeJournalData(journalData, lastfilesetid, GetTransaction()));
        }

        public Task<bool> IsBlocklistHashKnownAsync(string hash)
        {
            return RunOnMain(() => m_database.IsBlocklistHashKnown(hash, GetTransaction()));
        }

    }
}

