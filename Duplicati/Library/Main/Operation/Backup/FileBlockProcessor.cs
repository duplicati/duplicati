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
        public static async Task Start(Snapshots.ISnapshotService snapshot, Options options, BackupDatabase database)
        {
            return AutomationExtensions.RunTask(
            new 
            {
                Input = ChannelMarker.ForRead<MetadataPreProcess.FileEntry>("AcceptedChangedFile"),
                LogChannel = ChannelMarker.ForWrite<LogMessage>("LogChannel"),
                ProgressChannel = ChannelMarker.ForWrite<ProgressEvent>("ProgressChannel"),
                BlockOutput = ChannelMarker.ForWrite<DataBlock>("OutputBlocks")
            },

            async self =>
            {
                var fileoffset = -1L;
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

                            fileoffset = -1;
                            using(var fs = snapshot.OpenRead(e.Path))
                            {
                                long fslen = -1;
                                try { fslen = fs.Length; }
                                catch (Exception ex) { await self.LogChannel.WriteAsync(LogMessage.Warning(string.Format("Failed to read file length for file {0}", e.Path), ex)); }

                                await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = fslen, Type = EventType.FileStarted });
                                send_close = true;

                                fileoffset = 0;
                                filehasher.Initialize();
                                var lastread = 0;
                                var buf = new byte[blocksize];

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
                                        var blkey = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));
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

                                    // Make sure the filehasher is done with the buf instance before we pass it on
                                    await pftask;
                                    await DataBlock.AddBlockToOutputAsync(self.BlockOutput, hashkey, hashdata, buf, 0, hint, true);
                                    buf = new byte[blocksize];
                                }
                            }

                            // If we have more than a single block of data, output the (trailing) blocklist
                            if (hashcollector.Count > 1)
                            {
                                var blkey = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));
                                blocklisthashes.Add(blkey);
                                await DataBlock.AddBlockToOutputAsync(self.BlockOutput, blkey, blocklistbuffer, 0, blocklistoffset, CompressionHint.Noncompressible, true);
                            }

                            m_result.SizeOfOpenedFiles += filesize;
                            filehasher.TransformFinalBlock(new byte[0], 0, 0);

                            var filekey = Convert.ToBase64String(filehasher.Hash);
                            if (oldHash != filekey)
                            {
                                if (oldHash == null)
                                    await self.LogChannel.WriteAsync(LogMessage.Verbose("New file {0}", e.Path));
                                else
                                    await self.LogChannel.WriteAsync(LogMessage.Verbose("File has changed {0}", e.Path));

                                if (e.OldId < 0)
                                {
                                    m_result.AddedFiles++;
                                    m_result.SizeOfAddedFiles += filesize;

                                    if (options.Dryrun)
                                        await self.LogChannel.WriteAsync(LogMessage.DryRun("Would add new file {0}, size {1}", e.Path, Library.Utility.Utility.FormatSizeString(filesize)));
                                }
                                else
                                {
                                    m_result.ModifiedFiles++;
                                    m_result.SizeOfModifiedFiles += filesize;

                                    if (options.Dryrun)
                                        await self.LogChannel.WriteAsync(LogMessage.DryRun("Would add changed file {0}, size {1}", e.Path, Library.Utility.Utility.FormatSizeString(filesize)));
                                }

                                AddFileToOutput(e.Path, filesize, e.LastWrite, e.MetaHashAndSize, hashcollector, filekey, blocklisthashes, self.BlockOutput, blocksize, database);
                                changed = true;
                            }
                            else if (metadatachanged)
                            {
                                await self.LogChannel.WriteAsync(LogMessage.Verbose("File has only metadata changes {0}", e.Path));
                                AddFileToOutput(e.Path, filesize, e.LastWrite, e.MetaHashAndSize, hashcollector, filekey, blocklisthashes, self.BlockOutput, blocksize, database);
                                changed = true;
                            }
                            else
                            {
                                // When we write the file to output, update the last modified time
                                oldModified = lastwrite;
                                await self.LogChannel.WriteAsync(LogMessage.Verbose("File has not changed {0}", e.Path));
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        if (ex.IsRetiredException())
                            return;
                        else
                            await self.LogChannel.WriteAsync(LogMessage.Warning(string.Format("Failed to process file {0}", e.Path), ex));                    
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
        private static async Task AddFileToOutput(string filename, long size, DateTime lastmodified, IMetahash metadata, IEnumerable<string> hashlist, string filehash, IEnumerable<string> blocklisthashes, IWriteChannel<DataBlock> channel, long blocksize, BackupDatabase database)
        {
            long metadataid = -1;
            long blocksetid = -1;

            if (metadata.Size > blocksize)
                throw new InvalidDataException(string.Format("Too large metadata, cannot handle more than {0} bytes", blocksize));

            await DataBlock.AddBlockToOutputAsync(channel, metadata.Hash, null, metadata.Blob, 0, (int)metadata.Size, CompressionHint.Default, false);
            await database.AddMetadatasetAsync(metadata.Hash, metadata.Size, ref metadataid);
            await database.AddBlocksetAsync(filehash, size, blocksize, hashlist, blocklisthashes, ref blocksetid);
            await database.AddFileAsync(filename, lastmodified, blocksetid, metadataid);
        }
    }
}

