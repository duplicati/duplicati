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
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;
using CoCoL;
using Duplicati.Library.Main.Operation.Common;
using System.Collections.Generic;


namespace Duplicati.Library.Main.Operation.Backup
{
    public class BackupDatabase : DatabaseCommon
    {
        private LocalBackupDatabase m_database;

        public BackupDatabase(LocalBackupDatabase database)
            : base(database)
        {
            m_database = database;
        }

        public Task<bool> AddBlockAsync(string hash, long size, long volumeid)
        {
            return RunOnMain(() => m_database.AddBlock(hash, size, volumeid, m_transaction));
        }

        public Task<string> GetFileHashAsync(long fileid)
        {
            return RunOnMain(() => m_database.GetFileHash(fileid));
        }

        public Task<bool> AddMetadatasetAsync(string hash, long size, ref long metadataid)
        {
            return RunOnMain(() => m_database.AddMetadataset(hash, size, out metadataid, m_transaction));
        }

        public Task AddDirectoryEntryAsync(string filename, long metadataid, DateTime lastModified)
        {
            return RunOnMain(() => m_database.AddDirectoryEntry(filename, metadataid, lastModified, m_transaction));
        }

        public Task AddSymlinkEntryAsync(string filename, long metadataid, DateTime lastModified)
        {
            return RunOnMain(() => m_database.AddSymlinkEntry(filename, metadataid, lastModified, m_transaction));
        }

        public Task<long> GetFileEntryAsync(string path, ref long oldModified, ref long lastFileSize, ref string oldMetahash, ref long oldMetasize)
        {
            return RunOnMain(() => m_database.GetFileEntry(path, out oldModified, out lastFileSize, out oldMetahash, out oldMetasize));
        }

        public Task AddBlocksetAsync(string filehash, long size, int blocksize, IEnumerable<string> hashlist, IEnumerable<string> blocklisthashes, ref long blocksetid)
        {
            return RunOnMain(() => m_database.AddBlockset(filehash, size, blocksize, hashlist, blocklisthashes, out blocksetid, m_transaction));
        }

        public Task AddFileAsync(string filename, DateTime lastmodified, long blocksetid, long metadataid)
        {
            return RunOnMain(() => m_database.AddFile(filename, lastmodified, blocksetid, metadataid, m_transaction));
        }


    }
}

