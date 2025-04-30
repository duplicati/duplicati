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

