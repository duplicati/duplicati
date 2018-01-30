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
        private TaskCompletionSource<long> m_tcs = new TaskCompletionSource<long>();
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
        public readonly BlockVolumeWriter BlockVolume;
        public readonly bool CreateIndexVolume;
        public readonly IEnumerable<string> BlocklistData;

        public VolumeUploadRequest(BlockVolumeWriter blockvolume, bool createindexvolume, IEnumerable<string> blocklistdata)
        {
            BlockVolume = blockvolume;
            CreateIndexVolume = createindexvolume;
            BlocklistData = blocklistdata;
        }

        public static string EncodeBlockListEntry(string hash, long size, byte[] data)
        {
            return hash + ":" + size + ":" + Convert.ToBase64String(data);
        }

        public static string DecodeBlockListEntryHash(string entry)
        {
            var ix = entry.IndexOf(':');
            return entry.Substring(0, ix);
        }

        public static Tuple<string, long, byte[]> DecodeBlockListEntry(string entry)
        {
            var els = entry.Split(new char[] { ':' }, 3);
            return new Tuple<string, long, byte[]>(
                els[0],
                long.Parse(els[1]),
                Convert.FromBase64String(els[2])
            );
        }
    }
   
    /// <summary>
    /// This class encapsulates all requests to the backend
    /// and ensures that the <code>AsynchronousUploadLimit</code> is honored
    /// </summary>
    internal static class BackendUploader
    {
        public static Task Run(Common.BackendHandler backend, Options options, Common.DatabaseCommon database, BackupResults results, Common.ITaskReader taskreader, StatsCollector stats)
        {
            return AutomationExtensions.RunTask(new
            {
                Input = Channels.BackendRequest.ForRead,
            },

            async self =>
            {
                var inProgress = new Queue<KeyValuePair<int, Task>>();
                var max_pending = options.AsynchronousUploadLimit == 0 ? long.MaxValue : options.AsynchronousUploadLimit;
                var noIndexFiles = options.IndexfilePolicy == Options.IndexFileStrategy.None;
                var active = 0;
                var lastSize = -1L;
                
                while(!await self.Input.IsRetiredAsync && await taskreader.ProgressAsync)
                {
                    try
                    {
                        var req = await self.Input.ReadAsync();

                        if (!await taskreader.ProgressAsync)
                            continue;
                        
                        KeyValuePair<int, Task> task = default(KeyValuePair<int, Task>);
                        if (req is VolumeUploadRequest)
                        {
                            lastSize = ((VolumeUploadRequest)req).BlockVolume.SourceSize;

                            if (noIndexFiles || !((VolumeUploadRequest)req).CreateIndexVolume)
                                task = new KeyValuePair<int, Task>(1, backend.UploadFileAsync(((VolumeUploadRequest)req).BlockVolume, null));
                            else
                                task = new KeyValuePair<int, Task>(2, backend.UploadFileAsync(((VolumeUploadRequest)req).BlockVolume, name => IndexVolumeCreator.CreateIndexVolume(name, options, database, ((VolumeUploadRequest)req).BlocklistData)));
                        }
                        else if (req is FilesetUploadRequest)
                            task = new KeyValuePair<int, Task>(1, backend.UploadFileAsync(((FilesetUploadRequest)req).Fileset));
                        else if (req is IndexVolumeUploadRequest)
                            task = new KeyValuePair<int, Task>(1, backend.UploadFileAsync(((IndexVolumeUploadRequest)req).IndexVolume));
                        else if (req is FlushRequest)
                        {
                            try
                            {
                                while(inProgress.Count > 0)
                                    await inProgress.Dequeue().Value;
                                active = 0;
                            }
                            finally
                            {
                                ((FlushRequest)req).SetFlushed(lastSize);
                            }
                        }

                        if (task.Value != null)
                        {
                            inProgress.Enqueue(task);
                            active += task.Key;
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
                        if (await Task.WhenAny(top.Value, Task.Delay(500)) != top.Value)
                        {
                            try
                            {
                                stats.SetBlocking(true);
                                await top.Value;
                            }
                            finally
                            {
                                stats.SetBlocking(false);
                            }
                        }

						active -= top.Key;
                    }
                }

                results.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);

                try
                {
                    stats.SetBlocking(true);
                    while (inProgress.Count > 0)
                        await inProgress.Dequeue().Value;
                }
                finally
                {
                    stats.SetBlocking(false);
                }
            });
                                
        }
    }
}

