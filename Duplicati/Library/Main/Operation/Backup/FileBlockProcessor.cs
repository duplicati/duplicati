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
using Duplicati.Library.Main.Operation.Common;
using System.Threading.Tasks;
using System.Collections.Generic;
using Duplicati.Library.Utility;
using System.Linq;
using Duplicati.Library.Interface;
using System.IO;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This class runs a process which opens a file and outputs blocks for processing
    /// </summary>
    internal static class FileBlockProcessor
    {
        public static  Task Start(Snapshots.ISnapshotService snapshot, Options options, BackupDatabase database, BackupStatsCollector stats)
        {
            return AutomationExtensions.RunTask(
            new 
            {
                Input = Channels.AcceptedChangedFile.ForRead,
                LogChannel = Common.Channels.LogChannel.ForWrite,
                ProgressChannel = Channels.ProgressEvents.ForWrite,
                BlockOutput = Channels.OutputBlocks.ForWrite
            },

            async self =>
            {
                var log = new LogWrapper(self.LogChannel);
                var blocksize = options.Blocksize;
                var filehasher = System.Security.Cryptography.HashAlgorithm.Create(options.FileHashAlgorithm);                    
                var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(options.BlockHashAlgorithm);                    

                while (true)
                {
                    var e = await self.Input.ReadAsync();
                    var send_close = false;
                    var filesize = 0L;

                    try
                    {
                        var hint = options.GetCompressionHintFromFilename(e.Path);
                        var oldHash = e.OldId < 0 ? null : await database.GetFileHashAsync(e.OldId);
                    
                        using(var blocklisthashes = new Library.Utility.FileBackedStringList())
                        using(var hashcollector = new Library.Utility.FileBackedStringList())
                        {    
                            var blocklistbuffer = new byte[blocksize];
                            var blocklistoffset = 0L;

                            using(var fs = snapshot.OpenRead(e.Path))
                            {
                                long fslen = -1;
                                try { fslen = fs.Length; }
                                catch (Exception ex) { await log.WriteWarningAsync(string.Format("Failed to read file length for file {0}", e.Path), ex); }

                                await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = fslen, Type = EventType.FileStarted });
                                send_close = true;

                                filehasher.Initialize();
                                var lastread = 0;
                                var buf = new byte[blocksize];
                                var lastupdate = DateTime.Now;

                                // Core processing loop, read blocks of data and hash individually
                                while((lastread = await fs.ForceStreamReadAsync(buf, blocksize)) != 0)
                                {
                                    // Run file hashing concurrently to squeeze a little extra concurrency out of it
                                    var pftask = Task.Run(() => filehasher.TransformBlock(buf, 0, lastread, buf, 0));

                                    var hashdata = blockhasher.ComputeHash(buf, 0, lastread);
                                    var hashkey = Convert.ToBase64String(hashdata);

                                    // If we have too many hashes, flush the blocklist
                                    if (blocklistbuffer.Length - blocklistoffset < hashdata.Length)
                                    {
                                        var blkey = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, (int)blocklistoffset));
                                        blocklisthashes.Add(blkey);
                                        await DataBlock.AddBlockToOutputAsync(self.BlockOutput, blkey, blocklistbuffer, 0, blocklistoffset, CompressionHint.Noncompressible, true);
                                        blocklistoffset = 0;
                                        blocklistbuffer = new byte[blocksize];
                                    }

                                    // Store the current hash in the blocklist
                                    Array.Copy(hashdata, 0, blocklistbuffer, blocklistoffset, hashdata.Length);
                                    blocklistoffset += hashdata.Length;
                                    hashcollector.Add(hashkey);
                                    filesize += lastread;

                                    // Don't spam updates
                                    if ((DateTime.Now - lastupdate).TotalSeconds > 10)
                                    {
                                        await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = filesize, Type = EventType.FileProgressUpdate });
                                        lastupdate = DateTime.Now;
                                    }

                                    // Make sure the filehasher is done with the buf instance before we pass it on
                                    await pftask;
                                    await DataBlock.AddBlockToOutputAsync(self.BlockOutput, hashkey, buf, 0, lastread, hint, true);
                                    buf = new byte[blocksize];
                                }
                            }

                            // If we have more than a single block of data, output the (trailing) blocklist
                            if (hashcollector.Count > 1)
                            {
                                var blkey = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, (int)blocklistoffset));
                                blocklisthashes.Add(blkey);
                                await DataBlock.AddBlockToOutputAsync(self.BlockOutput, blkey, blocklistbuffer, 0, blocklistoffset, CompressionHint.Noncompressible, true);
                            }

                            await stats.AddOpenedFile(filesize);
                            filehasher.TransformFinalBlock(new byte[0], 0, 0);

                            var filekey = Convert.ToBase64String(filehasher.Hash);
                            if (oldHash != filekey)
                            {
                                if (oldHash == null)
                                    await log.WriteVerboseAsync("New file {0}", e.Path);
                                else
                                    await log.WriteVerboseAsync("File has changed {0}", e.Path);

                                if (e.OldId < 0)
                                {
                                    await stats.AddAddedFile(filesize);

                                    if (options.Dryrun)
                                        await log.WriteDryRunAsync("Would add new file {0}, size {1}", e.Path, Library.Utility.Utility.FormatSizeString(filesize));
                                }
                                else
                                {
                                    await stats.AddModifiedFile(filesize);

                                    if (options.Dryrun)
                                        await log.WriteDryRunAsync("Would add changed file {0}, size {1}", e.Path, Library.Utility.Utility.FormatSizeString(filesize));
                                }

                                await AddFileToOutputAsync(e.Path, filesize, e.LastWrite, e.MetaHashAndSize, hashcollector, filekey, blocklisthashes, self.BlockOutput, blocksize, database);
                            }
                            else if (e.MetadataChanged)
                            {
                                await log.WriteVerboseAsync("File has only metadata changes {0}", e.Path);
                                await AddFileToOutputAsync(e.Path, filesize, e.LastWrite, e.MetaHashAndSize, hashcollector, filekey, blocklisthashes, self.BlockOutput, blocksize, database);
                            }
                            else
                            {
                                // When we write the file to output, update the last modified time
                                await log.WriteVerboseAsync("File has not changed {0}", e.Path);
                                await database.AddUnmodifiedAsync(e.OldId, e.LastWrite);
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        if (ex.IsRetiredException())
                            return;
                        else
                            await log.WriteWarningAsync(string.Format("Failed to process file {0}", e.Path), ex);
                    }
                    finally
                    {
                        if (send_close)
                            await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = filesize, Type = EventType.FileClosed });
                    }
                }
            }
            );


        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private static async Task AddFileToOutputAsync(string filename, long size, DateTime lastmodified, IMetahash metadata, IEnumerable<string> hashlist, string filehash, IEnumerable<string> blocklisthashes, IWriteChannel<DataBlock> channel, int blocksize, BackupDatabase database)
        {
            if (metadata.Size > blocksize)
                throw new InvalidDataException(string.Format("Too large metadata, cannot handle more than {0} bytes", blocksize));

            await DataBlock.AddBlockToOutputAsync(channel, metadata.Hash, metadata.Blob, 0, (int)metadata.Size, CompressionHint.Default, false);
            var metadataid = await database.AddMetadatasetAsync(metadata.Hash, metadata.Size);
            var blocksetid = await database.AddBlocksetAsync(filehash, size, blocksize, hashlist, blocklisthashes);
            await database.AddFileAsync(filename, lastmodified, blocksetid, metadataid.Item2);
        }
    }
}

