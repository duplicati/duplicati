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

using System;
using CoCoL;
using Duplicati.Library.Main.Operation.Common;
using System.Threading.Tasks;
using System.Collections.Generic;
using Duplicati.Library.Utility;
using System.Linq;
using Duplicati.Library.Interface;
using System.IO;
using System.Security.Cryptography;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal static class StreamBlockSplitter
    {
        /// <summary>
        /// The tag used for log messages
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(StreamBlockSplitter));
        private static readonly string FILELOGTAG = LOGTAG + ".FileEntry";

        public static Task Run(Channels channels, Options options, BackupDatabase database, ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.StreamBlock.AsRead(),
                ProgressChannel = channels.ProgressEvents.AsWrite(),
                BlockOutput = channels.OutputBlocks.AsWrite()
            },

            async self =>
            {
                var blocksize = options.Blocksize;
                var emptymetadata = Utility.WrapMetadata(new Dictionary<string, string>(), options);
                var maxmetadatasize = (options.Blocksize / (long)options.BlockhashSize) * options.Blocksize;

                using (var filehasher = HashFactory.CreateHasher(options.FileHashAlgorithm))
                using (var blockhasher = HashFactory.CreateHasher(options.BlockHashAlgorithm))
                using (var empty_metadata_stream = new MemoryStream(emptymetadata.Blob))
                {
                    while (true)
                    {
                        // We ignore the stop signal, but not the pause and terminate
                        await taskreader.ProgressRendevouz().ConfigureAwait(false);
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
                                try
                                {
                                    fslen = stream.Length;
                                }
                                catch (Exception ex)
                                {
                                    Logging.Log.WriteWarningMessage(FILELOGTAG, "FileLengthFailure", ex, "Failed to read file length for file {0}", e.Path);
                                }

                                if (e.IsMetadata && fslen > maxmetadatasize)
                                {
                                    //TODO: To fix this, the "WriteFileset" method in BackupHandler needs to
                                    // be updated such that it can select sets even when there are multiple
                                    // blocklist hashes for the metadata.
                                    // This could be done such that an extra query is made if the metadata
                                    // spans multiple blocklist hashes, as it is not expected to be common

                                    Logging.Log.WriteWarningMessage(LOGTAG, "TooLargeMetadata", null, "Metadata size is {0}, but the largest accepted size is {1}, recording empty metadata for {2}", fslen, maxmetadatasize, e.Path);
                                    empty_metadata_stream.Position = 0;
                                    stream = empty_metadata_stream;
                                    fslen = stream.Length;
                                }

                                // Don't send progress reports for metadata
                                if (!e.IsMetadata)
                                {
                                    await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = fslen, Type = EventType.FileStarted });
                                    send_close = true;
                                }

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
                                    if (send_close && (DateTime.Now - lastupdate).TotalSeconds > 5)
                                    {
                                        await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = filesize, Type = EventType.FileProgressUpdate });
                                        lastupdate = DateTime.Now;
                                    }

                                    // Make sure the filehasher is done with the buf instance before we pass it on
                                    await pftask.ConfigureAwait(false);
                                    await DataBlock.AddBlockToOutputAsync(self.BlockOutput, hashkey, buf, 0, lastread, e.Hint, false);
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
                            catch
                            {
                            }

                            // Rethrow
                            if (ex.IsRetiredException())
                                throw;
                        }
                        finally
                        {
                            if (cur != null)
                            {
                                try
                                {
                                    cur.TrySetCanceled();
                                }
                                catch
                                {
                                }

                                cur = null;
                            }

                            if (send_close)
                                await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = filesize, Type = EventType.FileClosed });
                            send_close = false;
                        }
                    }
                }
            });
        }
    }
}
