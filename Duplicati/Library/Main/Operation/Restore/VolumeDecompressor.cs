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
using System.Threading;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Utility;

#nullable enable

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
        /// Id of the next decompressor. Used to give each decompressor a unique index.
        /// </summary>
        public static int IdCounter = -1;
        /// <summary>
        /// Maximum processing times for each active decompressor.
        /// </summary>
        public static int[] MaxProcessingTimes = [];

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
                Stopwatch? sw_read = options.InternalProfiling ? new() : null;
                Stopwatch? sw_write = options.InternalProfiling ? new() : null;
                Stopwatch? sw_decompress_alloc = options.InternalProfiling ? new() : null;
                Stopwatch? sw_decompress_instantiate = options.InternalProfiling ? new() : null;
                Stopwatch? sw_decompress_locking = options.InternalProfiling ? new() : null;
                Stopwatch? sw_decompress_read = options.InternalProfiling ? new() : null;
                Stopwatch? sw_verify = options.InternalProfiling ? new() : null;

                Stopwatch sw_processing = new();
                int id = Interlocked.Increment(ref IdCounter);

                try
                {
                    using var block_hasher = HashFactory.CreateHasher(options.BlockHashAlgorithm);

                    while (true)
                    {
                        sw_read?.Start();
                        // Get the block request and volume from the `VolumeDecryptor` process.
                        var (block_request, volume) = await self.Input.ReadAsync().ConfigureAwait(false);
                        using var _ = volume;
                        sw_read?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DecompressBlock", "Decompressing block {0} from volume {1}", block_request.BlockID, block_request.VolumeID);

                        sw_processing.Restart();
                        sw_decompress_alloc?.Start();
                        var data = ArrayPool<byte>.Shared.Rent(options.Blocksize);
                        sw_decompress_alloc?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DecompressBlock", "Allocated buffer for block {0} from volume {1}", block_request.BlockID, block_request.VolumeID);

                        sw_decompress_instantiate?.Start();
                        var block = new DataBlock(data);
                        sw_decompress_instantiate?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DecompressBlock", "Instantiated DataBlock for block {0} from volume {1}", block_request.BlockID, block_request.VolumeID);

                        sw_decompress_locking?.Start();
                        lock (volume.Reader!) // The BlockVolumeReader is not thread-safe
                        {
                            sw_decompress_locking?.Stop();
                            sw_decompress_read?.Start();
                            volume.Reader.ReadBlock(block_request.BlockHash, data);
                            sw_decompress_read?.Stop();
                        }
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DecompressBlock", "Decompressed block {0} from volume {1}", block_request.BlockID, block_request.VolumeID);

                        sw_verify?.Start();
                        var hash = Convert.ToBase64String(block_hasher.ComputeHash(data, 0, (int)block_request.BlockSize));
                        if (hash != block_request.BlockHash)
                        {
                            Logging.Log.WriteErrorMessage(LOGTAG, "InvalidBlock", null, "Invalid block detected for block {0} in volume {1}, expected hash: {2}, actual hash: {3}", block_request.BlockID, block_request.VolumeID, block_request.BlockHash, hash);
                        }
                        sw_verify?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DecompressBlock", "Verified block {0} from volume {1}", block_request.BlockID, block_request.VolumeID);
                        sw_processing.Stop();
                        // This is the only writing process to that int, so an update is safe.
                        MaxProcessingTimes[id] = Math.Max(MaxProcessingTimes[id], (int)sw_processing.ElapsedMilliseconds);

                        sw_write?.Start();
                        // Send the block to the `BlockManager` process.
                        await self.Output.WriteAsync((block_request, block)).ConfigureAwait(false);
                        sw_write?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DecompressBlock", "Sent block {0} from volume {1} to BlockManager", block_request.BlockID, block_request.VolumeID);
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume decompressor retired");

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Read: {sw_read!.ElapsedMilliseconds}ms, Write: {sw_write!.ElapsedMilliseconds}ms, Decompress allocate: {sw_decompress_alloc!.ElapsedMilliseconds}ms, Decompress instantiate: {sw_decompress_instantiate!.ElapsedMilliseconds}ms, Decompress lock: {sw_decompress_locking!.ElapsedMilliseconds}ms, Decompress read: {sw_decompress_read!.ElapsedMilliseconds}ms, Verify: {sw_verify!.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "DecompressionError", ex, "Error during decompression");
                    self.Input.Retire();
                    self.Output.Retire();
                    throw;
                }
            });
        }
    }

}