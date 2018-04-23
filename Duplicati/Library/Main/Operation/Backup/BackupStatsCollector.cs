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
using Duplicati.Library.Main.Operation.Common;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// Asynchronous interface that ensures all stat requests
    /// are performed in a sequential manner
    /// </summary>
    internal class BackupStatsCollector : StatsCollector
    {
        private BackupResults m_res;

        public BackupStatsCollector(BackupResults res)
            : base(res.BackendWriter)
        {
            m_res = res;
        }

        public Task AddOpenedFile(long size)
        {
            return RunOnMain(() => {
                m_res.SizeOfOpenedFiles += size;
                m_res.OpenedFiles++;
            });
        }

        public Task AddAddedFile(long size)
        {
            return RunOnMain(() => {
                m_res.SizeOfAddedFiles += size;
                m_res.AddedFiles++;
            });
        }

        public Task AddModifiedFile(long size)
        {
            return RunOnMain(() => {
                m_res.SizeOfModifiedFiles += size;
                m_res.ModifiedFiles++;
            });
        }

        public Task AddExaminedFile(long size)
        {
            return RunOnMain(() => {
                m_res.SizeOfExaminedFiles += size;
                m_res.ExaminedFiles++;
            });
        }
    }
}

