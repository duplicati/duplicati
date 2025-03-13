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

using CoCoL;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Volumes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal class SpillVolumeRequest
    {
        public BlockVolumeWriter BlockVolume { get; private set; }
        public TemporaryIndexVolume IndexVolume { get; private set; }

        public SpillVolumeRequest(BlockVolumeWriter blockvolume, TemporaryIndexVolume indexvolume)
        {
            BlockVolume = blockvolume;
            IndexVolume = indexvolume;
        }
    }

    /// <summary>
    /// This process just waits until all block processes are terminated
    /// and collects the non-written volumes.
    /// All remaining volumes are re-packed into one or more filled
    /// volumes and uploaded
    /// </summary>
    internal static class SpillCollectorProcess
    {
        public static Task Run(Channels channels, Options options, LocalBackupDatabase database, IBackendManager backendManager, ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.SpillPickup.AsRead()
            },

            async self =>
            {
                var lst = new List<SpillVolumeRequest>();

                while (true)
                    try
                    {
                        lst.Add(await self.Input.ReadAsync().ConfigureAwait(false));
                    }
                    catch (Exception ex) when (ex.IsRetiredException())
                    {
                        break;
                    }

                async Task<SpillVolumeRequest> GetNextTarget(SpillVolumeRequest source)
                {
                    SpillVolumeRequest target = null;
                    if (lst.Count == 0)
                    {
                        // No more targets, make one
                        target = new SpillVolumeRequest(new BlockVolumeWriter(options), source.IndexVolume == null ? null : new TemporaryIndexVolume(options));
                        using (await database.LockAsync())
                            target.BlockVolume.VolumeID = database.RegisterRemoteVolume(target.BlockVolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary);
                    }
                    else
                    {
                        // Grab the next target
                        target = lst[0];
                        lst.RemoveAt(0);
                    }

                    // We copy all blocklisthashes as they are pre-filtered to be unique
                    if (source.IndexVolume != null)
                        source.IndexVolume.CopyTo(target.IndexVolume, true);

                    return target;
                }


                while (lst.Count > 1)
                {
                    // We ignore the stop signal, but not the pause and terminate
                    await taskreader.ProgressRendevouz().ConfigureAwait(false);

                    SpillVolumeRequest target = null;
                    var source = lst[0];

                    // Finalize the current work
                    source.BlockVolume.Close();

                    // Remove it from the list of active operations
                    lst.RemoveAt(0);

                    var buffer = new byte[options.Blocksize];

                    using (var rd = new BlockVolumeReader(options.CompressionModule, source.BlockVolume.LocalFilename, options))
                    {
                        // Make sure we process the blocklisthashes, even if the blockvolume is empty
                        target = await GetNextTarget(source);

                        foreach (var file in rd.Blocks)
                        {
                            // Grab a target
                            if (target == null)
                                target = await GetNextTarget(source);

                            var len = rd.ReadBlock(file.Key, buffer);
                            target.BlockVolume.AddBlock(file.Key, buffer, 0, len, Duplicati.Library.Interface.CompressionHint.Default);
                            using (await database.LockAsync())
                                database.MoveBlockToVolume(file.Key, len, source.BlockVolume.VolumeID, target.BlockVolume.VolumeID);

                            if (target.IndexVolume != null)
                                target.IndexVolume.AddBlock(file.Key, len);

                            if (target.BlockVolume.Filesize > options.VolumeSize - options.Blocksize)
                            {
                                target.BlockVolume.Close();
                                await UploadVolumeAndIndex(target, options, database, backendManager, taskreader).ConfigureAwait(false);
                                target = null;
                            }
                        }
                    }

                    // Make sure they are out of the database
                    System.IO.File.Delete(source.BlockVolume.LocalFilename);
                    using (await database.LockAsync())
                        database.SafeDeleteRemoteVolume(source.BlockVolume.RemoteFilename);

                    // Re-inject the target if it has content
                    if (target != null)
                        lst.Insert(lst.Count == 0 ? 0 : 1, target);
                }

                foreach (var n in lst)
                {
                    // We ignore the stop signal, but not the pause and terminate
                    await taskreader.ProgressRendevouz().ConfigureAwait(false);

                    n.BlockVolume.Close();
                    await UploadVolumeAndIndex(n, options, database, backendManager, taskreader).ConfigureAwait(false);
                }

            });
        }

        private static async Task UploadVolumeAndIndex(SpillVolumeRequest target, Options options, LocalBackupDatabase database, IBackendManager backendManager, ITaskReader taskreader)
        {
            IndexVolumeWriter indexVolumeCopy = null;
            if (target.IndexVolume != null)
            {
                // TODO: It is much easier to let the BackendManager deal with index files,
                // but it adds a bit of strain to the database
                indexVolumeCopy = target.IndexVolume.CreateVolume(target.BlockVolume.RemoteFilename, options);

                using (await database.LockAsync())
                {
                    indexVolumeCopy.VolumeID = database.RegisterRemoteVolume(indexVolumeCopy.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary);
                    // Create link before upload is started, it will be removed later if upload fails
                    database.AddIndexBlockLink(indexVolumeCopy.VolumeID, target.BlockVolume.VolumeID);
                }
            }

            await backendManager.PutAsync(target.BlockVolume, indexVolumeCopy, null, false, taskreader.ProgressToken).ConfigureAwait(false);
        }
    }
}
