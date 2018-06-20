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
using CoCoL;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Volumes;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal interface IUploadRequest
    {
    }

    internal class FlushRequest : IUploadRequest
    {
        public Task<long> LastWriteSizeAync { get { return m_tcs.Task; } }
        private readonly TaskCompletionSource<long> m_tcs = new TaskCompletionSource<long>();
        public void SetFlushed(long size)
        {
            m_tcs.TrySetResult(size);
        }

    }

    internal class IndexVolumeUploadRequest : IUploadRequest
    {
        public IndexVolumeWriter IndexVolume { get; private set; }

        public IndexVolumeUploadRequest(IndexVolumeWriter indexVolume)
        {
            IndexVolume = indexVolume;
        }
    }

    internal class FilesetUploadRequest : IUploadRequest
    {
        public FilesetVolumeWriter Fileset { get; private set; }

        public FilesetUploadRequest(FilesetVolumeWriter fileset)
        {
            Fileset = fileset;
        }
    }

    internal class VolumeUploadRequest : IUploadRequest
    {
        public BlockVolumeWriter BlockVolume { get; private set; }
        public TemporaryIndexVolume IndexVolume { get; private set;}

        public VolumeUploadRequest(BlockVolumeWriter blockvolume, TemporaryIndexVolume indexvolume)
        {
            BlockVolume = blockvolume;
            IndexVolume = indexvolume;
        }
    }

    /// <summary>
    /// This class encapsulates all requests to the backend
    /// and ensures that the <code>AsynchronousUploadLimit</code> is honored
    /// </summary>
    internal static class BackendUploader
    {
        /// <summary>
        /// Structure for keeping queue data
        /// </summary>
        private struct TaskEntry
        {
            /// <summary>
            /// The number of items in this entry
            /// </summary>
            public int Items;
            /// <summary>
            /// The size of the items in this entry
            /// </summary>
            public long Size;
            /// <summary>
            /// The task to await for completion
            /// </summary>
            public Task Task;

            /// <summary>
            /// Initializes a new instance of the
            /// <see cref="T:Duplicati.Library.Main.Operation.Backup.BackendUploader.TaskEntry"/> struct.
            /// </summary>
            /// <param name="items">The number of items.</param>
            /// <param name="size">The size of the items.</param>
            /// <param name="task">The task.</param>
            public TaskEntry(int items, long size, Task task)
            {
                Items = items;
                Size = size;
                Task = task;
            }
        }

        public static Task Run(Common.BackendHandler backend, Options options, Common.DatabaseCommon database, BackupResults results, Common.ITaskReader taskreader, StatsCollector stats)
        {
            return AutomationExtensions.RunTask(new
            {
                Input = Channels.BackendRequest.ForRead,
            },

            async self =>
            {
                var inProgress = new Queue<TaskEntry>();
                var max_pending = options.AsynchronousUploadLimit == 0 ? long.MaxValue : options.AsynchronousUploadLimit;
                var noIndexFiles = options.IndexfilePolicy == Options.IndexFileStrategy.None;
                var active = 0;
                var queueSize = 0L;

                while (!await self.Input.IsRetiredAsync && await taskreader.ProgressAsync)
                {
                    try
                    {
                        var req = await self.Input.ReadAsync();
                        if (!await taskreader.ProgressAsync)
                            continue;

                        var task = default(TaskEntry);
                        if (req is VolumeUploadRequest)
                        {
                            var r = (VolumeUploadRequest)req;
                            if (noIndexFiles || r.IndexVolume == null)
                                task = new TaskEntry(1, r.BlockVolume.Filesize, backend.UploadFileAsync(r.BlockVolume, null));
                            else
                                task = new TaskEntry(2, r.BlockVolume.Filesize, backend.UploadFileAsync(r.BlockVolume, name => r.IndexVolume.CreateVolume(name, options, database)));
                        }
                        else if (req is FilesetUploadRequest)
                        {
                            task = new TaskEntry(1, ((FilesetUploadRequest)req).Fileset.Filesize, backend.UploadFileAsync(((FilesetUploadRequest) req).Fileset));
                        }
                        else if (req is IndexVolumeUploadRequest)
                        {
                            task = new TaskEntry(1, ((IndexVolumeUploadRequest)req).IndexVolume.Filesize, backend.UploadFileAsync(((IndexVolumeUploadRequest)req).IndexVolume));
                        }
                        else if (req is FlushRequest)
                        {
                            var flushed = 0L;
                            try
                            {
                                stats.SetBlocking(true);
                                while (inProgress.Count > 0)
                                {
                                    await inProgress.Peek().Task;
                                    var t = inProgress.Dequeue();

                                    flushed += t.Size;
                                    active -= t.Items;
                                    queueSize -= t.Size;
                                    stats.SetQueueSize(active, queueSize);
                                }
                            }
                            finally
                            {
                                stats.SetBlocking(false);
                                ((FlushRequest)req).SetFlushed(flushed);
                            }
                        }

                        if (task.Task != null)
                        {
                            inProgress.Enqueue(task);
                            active += task.Items;
                            queueSize += task.Size;
                            stats.SetQueueSize(active, queueSize);
                        }
                    }
                    catch(Exception ex)
                    {
                        if (!ex.IsRetiredException())
                            throw;
                    }


                    while(active >= max_pending)
                    {
                        var top = inProgress.Dequeue();

                        // See if we are done
                        if (await Task.WhenAny(top.Task, Task.Delay(500)) != top.Task)
                        {
                            try
                            {
                                stats.SetBlocking(true);
                                await top.Task;
                            }
                            finally
                            {
                                stats.SetBlocking(false);
                            }
                        }

                        active -= top.Items;
                        queueSize -= top.Size;
                        stats.SetQueueSize(active, queueSize);
                    }
                }

                results.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);

                try
                {
                    stats.SetBlocking(true);
                    while (inProgress.Count > 0)
                    {
                        var t = inProgress.Dequeue();
                        await t.Task;

                        active -= t.Items;
                        queueSize -= t.Size;
                        stats.SetQueueSize(active, queueSize);
                    }
                }
                finally
                {
                    stats.SetBlocking(false);
                }
            });
                                
        }
    }
}

