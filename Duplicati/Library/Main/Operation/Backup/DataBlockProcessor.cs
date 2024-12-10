// Copyright (C) 2024, The Duplicati Team
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
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Volumes;
using System;
using System.Threading.Tasks;
using static Duplicati.Library.Main.Operation.Common.BackendHandler;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This class receives data blocks, registers then in the database.
    /// New blocks are added to a compressed archive and sent
    /// to the uploader
    /// </summary>
    internal static class DataBlockProcessor
    {
        public static Task Run(Channels channels, BackupDatabase database, Options options, ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.OutputBlocks.AsRead(),
                Output = channels.BackendRequest.AsWrite(),
                SpillPickup = channels.SpillPickup.AsWrite(),
            },

            async self =>
            {
                var noIndexFiles = options.IndexfilePolicy == Options.IndexFileStrategy.None;
                var fullIndexFiles = options.IndexfilePolicy == Options.IndexFileStrategy.Full;
                var blocklistHashesAdded = 0L;

                BlockVolumeWriter blockvolume = null;
                TemporaryIndexVolume indexvolume = null;

                System.Diagnostics.Stopwatch sw_workload = new();
                long allowed_workload_ms = options.CPUIntensity * 100;

                try
                {
                    while (true)
                    {
                        var b = await self.Input.ReadAsync();

                        // Check if the process has spent more than allowed workload time
                        if (options.CPUIntensity < 10 && sw_workload.ElapsedMilliseconds > allowed_workload_ms)
                        {
                            // Sleep the remaining time
                            int time_to_sleep = 1000 - (int)allowed_workload_ms;
                            await Task.Delay(time_to_sleep);
                            sw_workload.Reset();
                        }
                        sw_workload.Start();

                        // We need these blocks to be stored in the full index files
                        var isMandatoryBlocklistHash = b.IsBlocklistHashes && fullIndexFiles;

                        // Lazy-start a new block volume
                        if (blockvolume == null)
                        {
                            // Before we start a new volume, probe to see if it exists
                            // This will delay creation of volumes for differential backups
                            // There can be a race, such that two workers determine that
                            // the block is missing, but this will be solved by the AddBlock call
                            // which runs atomically
                            if (!isMandatoryBlocklistHash && await database.FindBlockIDAsync(b.HashKey, b.Size) >= 0)
                            {
                                b.TaskCompletion.TrySetResult(false);
                                sw_workload.Stop();
                                continue;
                            }

                            blockvolume = new BlockVolumeWriter(options);
                            blockvolume.VolumeID = await database.RegisterRemoteVolumeAsync(blockvolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary);

                            indexvolume = noIndexFiles ? null : new TemporaryIndexVolume(options);
                            blocklistHashesAdded = 0;
                        }

                        var newBlock = await database.AddBlockAsync(b.HashKey, b.Size, blockvolume.VolumeID);
                        b.TaskCompletion.TrySetResult(newBlock);

                        // If we are recording blocklist hashes, add them to the index file,
                        // even if they are already added as non-blocklist blocks, but filter out duplicates
                        if (indexvolume != null && isMandatoryBlocklistHash)
                        {
                            // This can cause a race between workers,
                            // but the side-effect is that the index files are slightly larger
                            if (newBlock || !await database.IsBlocklistHashKnownAsync(b.HashKey))
                            {
                                blocklistHashesAdded++;
                                indexvolume.AddBlockListHash(b.HashKey, b.Size, b.Data);
                            }
                        }

                        if (newBlock)
                        {
                            blockvolume.AddBlock(b.HashKey, b.Data, b.Offset, (int)b.Size, b.Hint);
                            if (indexvolume != null)
                                indexvolume.AddBlock(b.HashKey, b.Size);

                            // If the volume is full, send to upload
                            if (blockvolume.Filesize > options.VolumeSize - options.Blocksize)
                            {
                                //When uploading a new volume, we register the volumes and then flush the transaction
                                // this ensures that the local database and remote storage are as closely related as possible
                                await database.UpdateRemoteVolumeAsync(blockvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null);

                                blockvolume.Close();

                                await database.CommitTransactionAsync("CommitAddBlockToOutputFlush");

                                FileEntryItem blockEntry = blockvolume.CreateFileEntryForUpload(options);

                                TemporaryIndexVolume indexVolumeCopy = null;
                                if (indexvolume != null)
                                {
                                    indexVolumeCopy = new TemporaryIndexVolume(options);
                                    indexvolume.CopyTo(indexVolumeCopy, false);
                                }

                                var uploadRequest = new VolumeUploadRequest(blockvolume, blockEntry, indexVolumeCopy, options, database);

                                blockvolume = null;
                                indexvolume = null;

                                // Write to output at the end here to prevent sending a full volume to the SpillCollector
                                await self.Output.WriteAsync(uploadRequest);
                            }

                        }

                        // We ignore the stop signal, but not the pause and terminate
                        await taskreader.ProgressRendevouz().ConfigureAwait(false);

                        sw_workload.Stop();
                    }
                }
                catch (Exception ex)
                {
                    if (ex.IsRetiredException())
                    {
                        // If we have collected data, merge all pending volumes into a single volume
                        if (blockvolume != null)
                        {
                            if (blockvolume.SourceSize > 0 || blocklistHashesAdded > 0)
                            {
                                await self.SpillPickup.WriteAsync(new SpillVolumeRequest(blockvolume, indexvolume));
                            }
                            else
                            {
                                await database.RemoveRemoteVolumeAsync(blockvolume.RemoteFilename);
                            }
                        }
                    }

                    throw;
                }
            });
        }
    }
}
