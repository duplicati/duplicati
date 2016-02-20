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
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Main.Operation.Common;
using System.IO;
using System.Linq;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This class receives data blocks and compresses them
    /// </summary>
    internal static class DataBlockProcessor
    {
        public static Task Run(BackupDatabase database, Options options)
        {
            return AutomationExtensions.RunTask(
                new
                {
                    LogChannel = ChannelMarker.ForWrite<LogMessage>("LogChannel"),
                    Input = ChannelMarker.ForRead<DataBlock>("OutputBlocks"),
                    Output = ChannelMarker.ForWrite<IBackendOperation>("BackendRequests"),
                    SpillPickup = ChannelMarker.ForWrite<IBackendOperation>("SpillPickup"),
                },

                async self =>
                {
                    BlockVolumeWriter blockvolume = null;
                    IndexVolumeWriter indexvolume = null;

                    try
                    {
                        while(true)
                        {
                            var b = await self.Input.ReadAsync();

                            // Lazy-start a new block volume
                            if (blockvolume == null)
                            {
                                // Before we start a new volume, probe to see if it exists
                                // This will delay creation of volumes for differential backups
                                // There can be a race, such that two workers determine that
                                // the block is missing, but this will be solved by the AddBlock call
                                // which runs atomically
                                if (await database.FindBlockIDAsync(b.HashKey, b.Size) >= 0)
                                {
                                    b.TaskCompletion.TrySetResult(false);
                                    continue;
                                }

                                blockvolume = new BlockVolumeWriter(options);
                                blockvolume.VolumeID = await database.RegisterRemoteVolumeAsync(blockvolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary);

                                if (options.IndexfilePolicy != Options.IndexFileStrategy.None)
                                {
                                    indexvolume = new IndexVolumeWriter(options);
                                    indexvolume.VolumeID = await database.RegisterRemoteVolumeAsync(indexvolume.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary);
                                }
                            }

                            var newBlock = await database.AddBlockAsync(b.HashKey, b.Size, blockvolume.VolumeID);
                            b.TaskCompletion.TrySetResult(newBlock);

                            if (newBlock)
                            {

                                blockvolume.AddBlock(b.HashKey, b.Data, b.Offset, (int)b.Size, b.Hint);

                                //TODO: In theory a normal data block and blocklist block could be equal.
                                // this would cause the index file to not contain all data,
                                // if the data file is added before the blocklist data
                                // ... highly theoretical and only causes extra block data downloads ...
                                if (options.IndexfilePolicy == Options.IndexFileStrategy.Full && b.IsBlocklistHashes)
                                    indexvolume.WriteBlocklist(b.HashKey, b.Data, b.Offset, (int)b.Size);

                                if (blockvolume.Filesize > options.VolumeSize - options.Blocksize)
                                {
                                    if (options.Dryrun)
                                    {
                                        blockvolume.Close();
                                            await self.LogChannel.WriteAsync(LogMessage.DryRun("Would upload block volume: {0}, size: {1}", blockvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(blockvolume.LocalFilename).Length)));

                                        if (indexvolume != null)
                                        {
                                            await database.UpdateIndexVolumeAsync(indexvolume, blockvolume);
                                            indexvolume.FinishVolume(Library.Utility.Utility.CalculateHash(blockvolume.LocalFilename), new FileInfo(blockvolume.LocalFilename).Length);
                                            await self.LogChannel.WriteAsync(LogMessage.DryRun("Would upload index volume: {0}, size: {1}", indexvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(indexvolume.LocalFilename).Length)));
                                            indexvolume.Dispose();
                                            indexvolume = null;
                                        }

                                        blockvolume.Dispose();
                                        blockvolume = null;
                                        indexvolume.Dispose();
                                        indexvolume = null;
                                    }
                                    else
                                    {
                                        //When uploading a new volume, we register the volumes and then flush the transaction
                                        // this ensures that the local database and remote storage are as closely related as possible
                                        await database.UpdateRemoteVolume(blockvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                                    
                                        blockvolume.Close();

                                        await database.UpdateIndexVolumeAsync(indexvolume, blockvolume);
                                        await database.CommitTransactionAsync("CommitAddBlockToOutputFlush");

                                        await self.Output.WriteAsync(new UploadRequest(blockvolume, indexvolume));
                                        blockvolume = null;
                                        indexvolume = null;
                                    }
                                }

                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        if (ex.IsRetiredException())
                        {
                            // If we have collected data, merge all pending volumes into a single volume
                            if (blockvolume != null && blockvolume.SourceSize > 0)
                                await self.SpillPickup.WriteAsync(new UploadRequest(blockvolume, indexvolume));
                        }

                        throw;
                    }
                }
            );
        }



    }
}

