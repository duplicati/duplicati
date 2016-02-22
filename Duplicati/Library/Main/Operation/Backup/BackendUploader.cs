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
    internal class UploadRequest
    {
        public BlockVolumeWriter BlockVolume { get; private set; }
        public IndexVolumeWriter IndexVolume { get; private set; }

        public UploadRequest(BlockVolumeWriter blockvolume, IndexVolumeWriter indexvolume)
        {
            BlockVolume = blockvolume;
            IndexVolume = indexvolume;
        }
    }

    internal static class BackendUploader
    {
        public static Task Run(Common.BackendHandler backend, Options options, Common.DatabaseCommon database, BackupResults results)
        {
            return AutomationExtensions.RunTask(new
                {
                    Input = ChannelMarker.ForRead<UploadRequest>("BackendRequests"),
                },

                async self =>
                {
                    var inProgress = new Queue<Task>();
                    var max_pending = options.AsynchronousUploadLimit == 0 ? long.MaxValue : options.AsynchronousUploadLimit;
                    if (options.IndexfilePolicy != Options.IndexFileStrategy.None)
                        max_pending = max_pending / 2;
                    
                    while(!self.Input.IsRetired)
                    {
                        try
                        {
                            var req = await self.Input.ReadAsync();
                            inProgress.Enqueue(backend.UploadFileAsync(req.BlockVolume, name => IndexVolumeCreator.CreateIndexVolume(name, options, database)));
                        }
                        catch(Exception ex)
                        {
                            if (!ex.IsRetiredException())
                                throw;
                        }
                        
                        while(inProgress.Count >= max_pending)
                            await inProgress.Dequeue();
                    }

                    results.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);

                    while(inProgress.Count > 0)
                        await inProgress.Dequeue();
                }
            );
                                
        }
    }
}

