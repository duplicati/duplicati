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
        public static Task Run(Options options, BackupDatabase database)
        {
            return AutomationExtensions.RunTask(
                new
                {
                    Input = ChannelMarker.ForRead<IBackendOperation>("SpillPickup"),
                    Output = ChannelMarker.ForWrite<IBackendOperation>("BackendRequests"),
                },

                async self => 
                {
                    var lst = new List<UploadRequest>();

                    while(!self.Input.IsRetired)
                        try
                        {
                    lst.Add((UploadRequest)await self.Input.ReadAsync());
                        }
                        catch (Exception ex)
                        {
                            if (ex.IsRetiredException())
                                break;
                            throw;
                        }


                    while(lst.Count > 1)
                    {

                        UploadRequest target = null;
                        var source = lst[0];

                        // Finalize the current work
                        source.BlockVolume.Close();

                        // We rebuild the index volume from the database
                        if (source.IndexVolume != null)
                            source.IndexVolume.Close();

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
                                        target = new UploadRequest(new BlockVolumeWriter(options), options.IndexfilePolicy == Options.IndexFileStrategy.None ? null : new IndexVolumeWriter(options));
                                        target.BlockVolume.VolumeID = await database.RegisterRemoteVolumeAsync(target.BlockVolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary);
                                        if (target.IndexVolume != null)
                                            target.IndexVolume.VolumeID = await database.RegisterRemoteVolumeAsync(target.IndexVolume.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary);
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
                                    if (options.IndexfilePolicy == Options.IndexFileStrategy.Full && target.IndexVolume != null && source.IndexVolume != null)
                                    {
                                        using(var ixr = DynamicLoader.CompressionLoader.GetModule(options.CompressionModule, source.IndexVolume.LocalFilename, options.RawOptions))
                                        foreach(var blocklisthash in await database.GetBlocklistHashesAsync(source.BlockVolume.RemoteFilename))
                                        {
                                            long fslen;
                                            using(var fs = ixr.OpenRead(blocklisthash))
                                            {
                                                target.IndexVolume.WriteBlocklist(blocklisthash, fs);
                                                fslen = fs.Length;
                                            }
                                            
                                            await database.MoveBlockToVolumeAsync(blocklisthash, fslen, source.BlockVolume.VolumeID, target.BlockVolume.VolumeID);
                                        }

                                    }

                                    await self.Output.WriteAsync(target);
                                    target = null;
                                }
                            }
                        }

                        // Make sure they are out of the database
                        System.IO.File.Delete(source.BlockVolume.LocalFilename);
                        await database.SafeDeleteRemoteVolumeAsync(source.BlockVolume.RemoteFilename);
                        if (source.IndexVolume != null)
                        {
                            System.IO.File.Delete(source.IndexVolume.LocalFilename);
                            await database.SafeDeleteRemoteVolumeAsync(source.IndexVolume.RemoteFilename);
                        }

                        // Re-inject the target if it has content
                        if (target != null)
                            lst.Insert(lst.Count == 0 ? 0 : 1, target);

                    }

                    foreach(var n in lst)
                        await self.Output.WriteAsync(n);

                }
            );
        }
    }
}

