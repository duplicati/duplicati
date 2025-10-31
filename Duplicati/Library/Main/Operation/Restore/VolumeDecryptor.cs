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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Volumes;

#nullable enable

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that decrypts the volumes that the `VolumeDownloader` process has downloaded.
    /// </summary>
    internal class VolumeDecryptor
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDecryptor>();

        /// <summary>
        /// Id of the next decryptor. Used to give each decryptor a unique index.
        /// </summary>
        public static int IdCounter = -1;
        /// <summary>
        /// Maximum processing times for each active decryptor.
        /// </summary>
        public static int[] MaxProcessingTimes = [];

        /// <summary>
        /// Runs the volume decryptor process.
        /// </summary>
        /// <param name="channels">The named channels for the restore operation.</param>
        /// <param name="backend">The backend manager.</param>
        /// <param name="options">The restore options.</param>
        public static Task Run(Channels channels, IBackendManager backend, Options options)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.DecryptRequest.AsRead(),
                Output = channels.VolumeResponse.AsWrite()
            },
            async self =>
            {
                Stopwatch? sw_read = options.InternalProfiling ? new() : null;
                Stopwatch? sw_write = options.InternalProfiling ? new() : null;
                Stopwatch? sw_decrypt = options.InternalProfiling ? new() : null;
                Stopwatch? sw_bvr = options.InternalProfiling ? new() : null;
                Stopwatch? sw_vw = options.InternalProfiling ? new() : null;

                Stopwatch sw_processing = new();
                int id = Interlocked.Increment(ref IdCounter);

                try
                {
                    while (true)
                    {
                        // Get the block request and volume from the `VolumeDownloader` process.
                        sw_read?.Start();
                        var (volume_id, volume_name, volume) = await self.Input.ReadAsync().ConfigureAwait(false);
                        sw_read?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DecryptVolume", null, "Decrypting volume {0} (ID: {1})", volume_name, volume_id);

                        sw_processing.Restart();
                        // Decrypt the volume.
                        sw_decrypt?.Start();
                        var tmpfile = backend.DecryptFile(volume, volume_name, options);
                        sw_decrypt?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DecryptVolume", null, "Decrypted volume {0} (ID: {1})", volume_name, volume_id);

                        // TODO These two steps could be moved to another process to allow even more parallelism.
                        sw_bvr?.Start();
                        var bvr = new BlockVolumeReader(options.CompressionModule, tmpfile, options);
                        sw_bvr?.Stop();
                        sw_vw?.Start();
                        var volume_wrapper = new VolumeWrapper(tmpfile, bvr);
                        sw_vw?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "BlockVolumeReader", null, "Created BlockVolumeReader for volume {0} (ID: {1})", volume_name, volume_id);
                        sw_processing.Stop();
                        // This is the only writing process to that int, so an update is safe.
                        MaxProcessingTimes[id] = Math.Max(MaxProcessingTimes[id], (int)sw_processing.ElapsedMilliseconds);

                        sw_write?.Start();
                        // Pass the decrypted volume to the `VolumeDecompressor` process.
                        await self.Output.WriteAsync((volume_id, volume_wrapper)).ConfigureAwait(false);
                        sw_write?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DecryptVolume", null, "Passed decrypted volume {0} (ID: {1}) to next stage", volume_name, volume_id);
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume decryptor retired");

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Read: {sw_read!.ElapsedMilliseconds}ms, Decrypt: {sw_decrypt!.ElapsedMilliseconds}ms, BlockVolumeReader: {sw_bvr!.ElapsedMilliseconds}ms, VolumeWrapper: {sw_vw!.ElapsedMilliseconds}ms, Write: {sw_write!.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "DecryptionError", ex, "Error during decryption");
                    self.Input.Retire();
                    self.Output.Retire();
                    throw;
                }
            });
        }
    }

}