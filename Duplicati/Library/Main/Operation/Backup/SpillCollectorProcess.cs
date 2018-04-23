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
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main.Volumes;
using System.IO;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This process just waits until all block processes are terminated
    /// and collects the non-written volumes.
    /// All remaining volumes are re-packed into one or more filled
    /// volumes and uploaded
    /// </summary>
    internal static class SpillCollectorProcess
    {
        public static Task Run(Options options, BackupDatabase database, ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.SpillPickup.ForRead,
                Output = Channels.BackendRequest.ForWrite,
            },

            async self => 
            {
                var lst = new List<VolumeUploadRequest>();

                while(!await self.Input.IsRetiredAsync)
                    try
                    {
                        lst.Add((VolumeUploadRequest)await self.Input.ReadAsync());
                    }
                    catch (Exception ex)
                    {
                        if (ex.IsRetiredException())
                            break;
                        throw;
                    }


                while(lst.Count > 1)
                {
                    // We ignore the stop signal, but not the pause and terminate
                    await taskreader.ProgressAsync;

                    VolumeUploadRequest target = null;
                    var source = lst[0];

                    // Finalize the current work
                    source.BlockVolume.Close();

                    // Remove it from the list of active operations
                    lst.RemoveAt(0);

                    var buffer = new byte[options.Blocksize];

                    using(var rd = new BlockVolumeReader(options.CompressionModule, source.BlockVolume.LocalFilename, options))
                    {
                        foreach(var file in rd.Blocks)
                        {
                            // Grab a target
                            if (target == null)
                            {
                                if (lst.Count == 0)
                                {
                                    // No more targets, make one
                                    target = new VolumeUploadRequest(new BlockVolumeWriter(options), true);
                                    target.BlockVolume.VolumeID = await database.RegisterRemoteVolumeAsync(target.BlockVolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary);
                                }
                                else
                                {
                                    // Grab the next target
                                    target = lst[0];
                                    lst.RemoveAt(0);
                                }
                            }


                            var len = rd.ReadBlock(file.Key, buffer);
                            target.BlockVolume.AddBlock(file.Key, buffer, 0, len, Duplicati.Library.Interface.CompressionHint.Default);
                            await database.MoveBlockToVolumeAsync(file.Key, len, source.BlockVolume.VolumeID, target.BlockVolume.VolumeID);

                            if (target.BlockVolume.Filesize > options.VolumeSize - options.Blocksize)
                            {
                                target.BlockVolume.Close();
                                await self.Output.WriteAsync(target);
                                target = null;
                            }
                        }
                    }

                    // Make sure they are out of the database
                    System.IO.File.Delete(source.BlockVolume.LocalFilename);
                    await database.SafeDeleteRemoteVolumeAsync(source.BlockVolume.RemoteFilename);

                    // Re-inject the target if it has content
                    if (target != null)
                        lst.Insert(lst.Count == 0 ? 0 : 1, target);

                }

                foreach(var n in lst)
                {
                    // We ignore the stop signal, but not the pause and terminate
                    await taskreader.ProgressAsync;

                    n.BlockVolume.Close();
                    await self.Output.WriteAsync(n);
                }

            });
        }
    }
}

