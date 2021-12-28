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

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// Ensures sequentially emitted events
    /// </summary>
    internal class StatsCollector : SingleRunner, IDisposable
    {
        protected readonly IBackendWriter m_bw;

        public StatsCollector(IBackendWriter bw)
        {
            m_bw = bw;
        }

        public Task SendEventAsync(BackendActionType action, BackendEventType type, string path, long size, bool updateProgress = true)
        {
            return RunOnMain(() => m_bw.SendEvent(action, type, path, size, updateProgress));
        }

        public void UpdateBackendStart(BackendActionType action, string path, long size)
        {
            m_bw.BackendProgressUpdater.StartAction(action, path, size);
        }

        public void UpdateBackendProgress(long pg)
        {
            m_bw.BackendProgressUpdater.UpdateProgress(pg);
        }

        public void UpdateBackendTotal(long size)
        {
            m_bw.BackendProgressUpdater.UpdateTotalSize(size);
        }

        public void SetBlocking(bool isBlocked)
        {
            if (m_bw.BackendProgressUpdater != null)
                m_bw.BackendProgressUpdater.SetBlocking(isBlocked);
        }
    }
}

