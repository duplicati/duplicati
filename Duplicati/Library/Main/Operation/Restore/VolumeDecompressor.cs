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
using System.Buffers;
using System.Diagnostics;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that decompresses the volumes that the `VolumeDecryptor` process has decrypted.
    /// </summary>
    internal class VolumeDecompressor
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDecompressor>();

        /// <summary>
        /// Runs the volume decompressor process.
        /// </summary>
        /// <param name="channels">The named channels for the restore operation.</param>
        /// <param name="options">The restore options</param>
        public static Task Run(Channels channels, Options options)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.DecompressionRequest.AsRead(),
                Output = channels.DecompressedBlock.AsWrite()
            },
            async self =>
            {
                Stopwatch sw_read = options.InternalProfiling ? new() : null;
                Stopwatch sw_write = options.InternalProfiling ? new() : null;
                Stopwatch sw_decompress_alloc = options.InternalProfiling ? new() : null;
                Stopwatch sw_decompress_locking = options.InternalProfiling ? new() : null;
                Stopwatch sw_decompress_read = options.InternalProfiling ? new() : null;
                Stopwatch sw_verify = options.InternalProfiling ? new() : null;

                try
                {
                    using var block_hasher = HashFactory.CreateHasher(options.BlockHashAlgorithm);

                    while (true)
                    {
                        sw_read?.Start();
                        // Get the block request and volume from the `VolumeDecryptor` process.
                        var (block_request, volume_reader) = await self.Input.ReadAsync().ConfigureAwait(false);
                        sw_read?.Stop();

                        sw_decompress_alloc?.Start();
                        var data = ArrayPool<byte>.Shared.Rent(options.Blocksize);
                        sw_decompress_alloc?.Stop();
                        sw_decompress_locking?.Start();
                        lock (volume_reader) // The BlockVolumeReader is not thread-safe
                        {
                            sw_decompress_locking?.Stop();
                            sw_decompress_read?.Start();
                            volume_reader.ReadBlock(block_request.BlockHash, data);
                            sw_decompress_read?.Stop();
                        }

                        sw_verify?.Start();
                        var hash = Convert.ToBase64String(block_hasher.ComputeHash(data, 0, (int)block_request.BlockSize));
                        if (hash != block_request.BlockHash)
                        {
                            Logging.Log.WriteErrorMessage(LOGTAG, "InvalidBlock", null, $"Invalid block detected for block {block_request.BlockID} in volume {block_request.VolumeID}, expected hash: {block_request.BlockHash}, actual hash: {hash}");
                        }
                        sw_verify?.Stop();

                        sw_write?.Start();
                        // Send the block to the `BlockManager` process.
                        await self.Output.WriteAsync((block_request, data)).ConfigureAwait(false);
                        sw_write?.Stop();
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume decompressor retired");

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Read: {sw_read.ElapsedMilliseconds}ms, Write: {sw_write.ElapsedMilliseconds}ms, Decompress allocate: {sw_decompress_alloc.ElapsedMilliseconds}ms, Decompress lock: {sw_decompress_locking.ElapsedMilliseconds}ms, Decompress read: {sw_decompress_read.ElapsedMilliseconds}ms, Verify: {sw_verify.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "DecompressionError", ex, "Error during decompression");
                    throw;
                }
            });
        }
    }

}