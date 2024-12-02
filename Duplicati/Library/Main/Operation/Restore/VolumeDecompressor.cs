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

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that decompresses the volumes that the `VolumeDecrypter` process has decrypted.
    /// </summary>
    internal class VolumeDecompressor
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDecompressor>();

        public static Task Run(Options options, RestoreResults results)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.decryptedVolume.ForRead,
                Output = Channels.decompressedVolumes.ForWrite
            },
            async self =>
            {
                Stopwatch sw_read       = options.InternalProfiling ? new () : null;
                Stopwatch sw_write      = options.InternalProfiling ? new () : null;
                Stopwatch sw_decompress = options.InternalProfiling ? new () : null;
                Stopwatch sw_decompress_1 = options.InternalProfiling ? new () : null;
                Stopwatch sw_decompress_2 = options.InternalProfiling ? new () : null;
                Stopwatch sw_verify     = options.InternalProfiling ? new () : null;

                try {
                    using var block_hasher = HashFactory.CreateHasher(options.BlockHashAlgorithm);

                    while (true)
                    {
                        sw_read?.Start();
                        // Get the block request and volume from the `VolumeDecrypter` process.
                        var (block_request, volume) = await self.Input.ReadAsync();
                        sw_read?.Stop();

                        if (block_request.CacheDecrEvict)
                        {
                            self.Output.Write((block_request, null));
                            continue;
                        }
                        sw_decompress_1?.Start();
                        var bvr = new BlockVolumeReader(options.CompressionModule, volume, options);
                        sw_decompress_1?.Stop();

                        sw_write?.Start();
                        // Send the block to the `BlockManager` process.
                        await self.Output.WriteAsync((block_request, bvr));
                        sw_write?.Stop();
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume decompressor retired");

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Read: {sw_read.ElapsedMilliseconds}ms, Write: {sw_write.ElapsedMilliseconds}ms, Decompress: {sw_decompress.ElapsedMilliseconds}ms, Verify: {sw_verify.ElapsedMilliseconds}ms");
                        Console.WriteLine($"Volume decompressor - Decompress: {sw_decompress.ElapsedMilliseconds}ms, Read: {sw_read.ElapsedMilliseconds}ms, Write: {sw_write.ElapsedMilliseconds}ms, Verify: {sw_verify.ElapsedMilliseconds}ms, Decompress1: {sw_decompress_1.ElapsedMilliseconds}ms, Decompress2: {sw_decompress_2.ElapsedMilliseconds}ms");
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