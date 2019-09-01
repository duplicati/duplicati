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
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal class FileProgressThrottler
    {
        private readonly Dictionary<string, File> m_Files = new Dictionary<string, File>();
        private readonly object m_Lock = new object();
        private readonly StatsCollector m_Stats;
        private long m_MaxBytesPerSecond;
        private long m_TotalSize;

        public FileProgressThrottler(StatsCollector stats, long maxBytesPerSecond)
        {
            m_Stats = stats;
            m_MaxBytesPerSecond = maxBytesPerSecond;
        }

        public void EndFileProgress(string path)
        {
            lock (m_Lock)
            {
                m_Files.TryGetValue(path, out var f);
                if (f != null)
                    f.Done = true;
            }
        }

        public void Run(CancellationToken cancelToken)
        {
            Task.Run(() => UpdateProgressAndThrottle(cancelToken));
        }

        public void StartFileProgress(string path, long size)
        {
            bool setStartProgress;
            long totalSize;

            lock (m_Lock)
            {
                m_Files.Add(path, new File());

                setStartProgress = m_TotalSize == 0;
                m_TotalSize += size;
                totalSize = m_TotalSize;
            }

            if (setStartProgress)
                m_Stats.UpdateBackendStart(BackendActionType.Put, "ParallelUpload", totalSize);
            else
                m_Stats.UpdateBackendTotal(totalSize);
        }

        public void UpdateFileProgress(string path, long offset, ThrottledStream stream)
        {
            lock (m_Lock)
            {
                m_Files.TryGetValue(path, out var f);
                if (f != null)
                {
                    f.Offset = offset;
                    if (f.Stream == null)
                        f.Stream = stream;
                }
            }
        }

        public void UpdateThrottleSpeeds(long maxUploadPrSecond)
        {
            lock (m_Lock)
            {
                m_MaxBytesPerSecond = maxUploadPrSecond;
            }
        }

        private void DecreaseTransferRate(long bytesTransferred)
        {
            var percentDecrease = (((double)100 / m_Files.Count()) / 100);
            var overAmount = bytesTransferred - m_MaxBytesPerSecond;

            foreach (var file in m_Files.OrderByDescending(f => f.Value.BytesPerSecond))
            {
                if (file.Value.Stream == null)
                    continue;

                if (overAmount <= 0)
                    break;

                var decreaseAmount = Math.Min(overAmount, (long)(file.Value.BytesPerSecond * percentDecrease));
                file.Value.Stream.ReadSpeed = file.Value.BytesPerSecond - decreaseAmount;

                overAmount -= decreaseAmount;
            }
        }

        private void IncreaseTransferRate(long bytesTransferred)
        {
            var percentIncrease = (((double)100 / m_Files.Count()) / 100);
            var underAmount = m_MaxBytesPerSecond - bytesTransferred;

            foreach (var file in m_Files.OrderByDescending(f => f.Value.BytesPerSecond))
            {
                if (file.Value.Stream == null)
                    continue;

                if (underAmount <= 0)
                    break;

                var increaseAmount = Math.Max(underAmount, (long)(file.Value.BytesPerSecond * percentIncrease));
                file.Value.Stream.ReadSpeed = file.Value.BytesPerSecond + increaseAmount;

                underAmount -= increaseAmount;
            }
        }

        private async Task UpdateProgressAndThrottle(CancellationToken cancelToken)
        {
            long startingOffset = 0;
            long endingOffset = 0;
            long bytesTransferred = 0;
            long totalBytesTransferred = 0;
            var toRemove = new List<string>(10);

            while (true)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (cancelToken.IsCancellationRequested)
                    return;

                lock (m_Lock)
                {
                    if (!m_Files.Any())
                        continue;

                    foreach (var kvp in m_Files)
                    {
                        var file = kvp.Value;
                        file.BytesPerSecond = file.Offset - file.PreviousOffset;
                        file.PreviousOffset = file.Offset;
                        endingOffset += file.BytesPerSecond;

                        if (file.Done)
                            toRemove.Add(kvp.Key);
                    }

                    foreach (var f in toRemove)
                        m_Files.Remove(f);

                    bytesTransferred = endingOffset - startingOffset;

                    if (m_MaxBytesPerSecond > 0)
                    {
                        if (bytesTransferred <= m_MaxBytesPerSecond)
                            IncreaseTransferRate(bytesTransferred);
                        else
                            DecreaseTransferRate(bytesTransferred);
                    }
                }

                toRemove.Clear();
                totalBytesTransferred += bytesTransferred;
                startingOffset = endingOffset;

                if (totalBytesTransferred > 0)
                    m_Stats.UpdateBackendProgress(totalBytesTransferred);
            }
        }

        private class File
        {
            public long BytesPerSecond;
            public bool Done;
            public long Offset;
            public long PreviousOffset;
            public ThrottledStream Stream;
        }
    }
}