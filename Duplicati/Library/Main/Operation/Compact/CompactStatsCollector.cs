//  Copyright (C) 2017, The Duplicati Team
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
using System.Collections.Generic;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;

namespace Duplicati.Library.Main.Operation.Compact
{
    internal class CompactStatsCollector : StatsCollector
    {
        private CompactResults m_res;

		public CompactStatsCollector(CompactResults res)
            : base(res.BackendWriter)
        {
			m_res = res;
		}

		public Task SetResultAsync(long DeletedFileCount, long DownloadedFileCount, long UploadedFileCount, long DeletedFileSize, long DownloadedFileSize, long UploadedFileSize, bool Dryrun)
		{
			return RunOnMain(() => {
				m_res.DeletedFileCount = DeletedFileCount;
				m_res.DownloadedFileCount = DownloadedFileCount;
				m_res.UploadedFileCount = UploadedFileCount;
				m_res.DeletedFileSize = DeletedFileSize;
				m_res.DownloadedFileSize = DownloadedFileSize;
				m_res.UploadedFileSize = UploadedFileSize;
				m_res.Dryrun = Dryrun;
			});
		}

        public Task SetEndTimeAsync()
        {
            return RunOnMain(() =>
            {
                m_res.EndTime = DateTime.UtcNow;
            });
        }

		public long DeletedFileCount
		{
			get { return m_res.DeletedFileCount; }
		}
		public long DownloadedFileCount
		{
			get { return m_res.DownloadedFileCount; }
		}
		public long UploadedFileCount
		{
			get { return m_res.UploadedFileCount; }
		}
		public long DeletedFileSize
		{
			get { return m_res.DeletedFileSize; }
		}
		public long DownloadedFileSize
		{
			get { return m_res.DownloadedFileSize; }
		}
		public long UploadedFileSize
		{
			get { return m_res.UploadedFileSize; }
		}
		public bool Dryrun
		{
			get { return m_res.Dryrun; }
		}
        public DateTime EndTime
		{
            get { return m_res.EndTime; }
		}

	}
}
