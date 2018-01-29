//  Copyright (C) 2016, The Duplicati Team
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
    internal static class StreamBlockSplitter
    {
        public static Task Run(Options options, BackupDatabase database, ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.StreamBlock.ForRead,
                LogChannel = Common.Channels.LogChannel.ForWrite,
                ProgressChannel = Channels.ProgressEvents.ForWrite,
                BlockOutput = Channels.OutputBlocks.ForWrite
            },

            async self =>
            {
                var log = new LogWrapper(self.LogChannel);
                var blocksize = options.Blocksize;
                var filehasher = Duplicati.Library.Utility.HashAlgorithmHelper.Create(options.FileHashAlgorithm);
                var blockhasher = Duplicati.Library.Utility.HashAlgorithmHelper.Create(options.BlockHashAlgorithm);
                var emptymetadata = Utility.WrapMetadata(new Dictionary<string, string>(), options);
                var maxmetadatasize = (options.Blocksize / (long)options.BlockhashSize) * options.Blocksize;

                 if (blockhasher == null)
                    throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(options.BlockHashAlgorithm));
                 if (filehasher == null)
                     throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(options.FileHashAlgorithm));
 
                 if (!blockhasher.CanReuseTransform)
                     throw new UserInformationException(Strings.Common.InvalidCryptoSystem(options.BlockHashAlgorithm));
                 if (!filehasher.CanReuseTransform)
                     throw new UserInformationException(Strings.Common.InvalidCryptoSystem(options.FileHashAlgorithm));

                using (var empty_metadata_stream = new MemoryStream(emptymetadata.Blob))
                while (await taskreader.ProgressAsync)
                {
                    var send_close = false;
                    var filesize = 0L;

                    var e = await self.Input.ReadAsync();
                    var cur = e.Result;

                    try
                    {
                        var stream = e.Stream;

                        using (var blocklisthashes = new Library.Utility.FileBackedStringList())
                        using (var hashcollector = new Library.Utility.FileBackedStringList())
                        {
                            var blocklistbuffer = new byte[blocksize];
                            var blocklistoffset = 0L;

                            long fslen = -1;
                            try { fslen = stream.Length; }
                            catch (Exception ex) { await log.WriteWarningAsync(string.Format("Failed to read file length for file {0}", e.Path), ex); }

                            if (e.IsMetadata && fslen > maxmetadatasize)
                            {
                                //TODO: To fix this, the "WriteFileset" method in BackupHandler needs to
                                // be updated such that it can select sets even when there are multiple
                                // blocklist hashes for the metadata.
                                // This could be done such that an extra query is made if the metadata
                                // spans multiple blocklist hashes, as it is not expected to be common

                                await log.WriteWarningAsync(string.Format("Metadata size is {0}, but the largest accepted size is {1}, recording empty metadata for {2}", fslen, maxmetadatasize, e.Path), null);
                                empty_metadata_stream.Position = 0;
                                stream = empty_metadata_stream;
                                fslen = stream.Length;
                            }

                            await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = fslen, Type = EventType.FileStarted });
                            send_close = true;

                            filehasher.Initialize();
                            var lastread = 0;
                            var buf = new byte[blocksize];
                            var lastupdate = DateTime.Now;

                            // Core processing loop, read blocks of data and hash individually
                            while (((lastread = await stream.ForceStreamReadAsync(buf, blocksize)) != 0))
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
                                await DataBlock.AddBlockToOutputAsync(self.BlockOutput, hashkey, buf, 0, lastread, e.Hint, true);
                                buf = new byte[blocksize];
                            }

                            // If we have more than a single block of data, output the (trailing) blocklist
                            if (hashcollector.Count > 1)
                            {
                                var blkey = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, (int)blocklistoffset));
                                blocklisthashes.Add(blkey);
                                await DataBlock.AddBlockToOutputAsync(self.BlockOutput, blkey, blocklistbuffer, 0, blocklistoffset, CompressionHint.Noncompressible, true);
                            }

                            filehasher.TransformFinalBlock(new byte[0], 0, 0);
                            var filehash = Convert.ToBase64String(filehasher.Hash);
                            var blocksetid = await database.AddBlocksetAsync(filehash, filesize, blocksize, hashcollector, blocklisthashes);
                            cur.SetResult(new StreamProcessResult() { Streamlength = filesize, Streamhash = filehash, Blocksetid = blocksetid });
                            cur = null;
                        }
                    }
                    catch (Exception ex)
                    {                        
                        try
                        {
                            if (cur != null)
                                cur.TrySetException(ex);
                        }
                        catch { }

                        // Rethrow
                        if (ex.IsRetiredException())
                            throw;
                    }
                    finally
                    {
                        if (cur != null)
                        {
                            try { cur.TrySetCanceled(); }
                            catch { }
                            cur = null;
                        }

                        if (send_close)
                            await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = filesize, Type = EventType.FileClosed });
                        send_close = false;
                    }
                }
            });
        }
    }
}
