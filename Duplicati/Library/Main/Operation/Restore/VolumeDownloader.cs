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
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that starts the download of the requested volumes.
    /// </summary>
    internal class VolumeDownloader
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDownloader>();

        /// <summary>
        /// Runs the volume downloader process.
        /// </summary>
        /// <param name="channels">The named channels for the restore operation.</param>
        /// <param name="db">The local restore database, used to find volume information given the volume ID.</param>
        /// <param name="backend">The backend to use for downloading the volumes.</param>
        /// <param name="options">The restore options.</param>
        /// <param name="results">The restore results.</param>
        public static Task Run(Channels channels, LocalRestoreDatabase db, IBackendManager backend, Options options, RestoreResults results)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.DownloadRequest.AsRead(),
                Output = channels.DecryptRequest.AsWrite()
            },
            async self =>
            {
                Stopwatch? sw_read = options.InternalProfiling ? new() : null;
                Stopwatch? sw_write = options.InternalProfiling ? new() : null;
                Stopwatch? sw_wait = options.InternalProfiling ? new() : null;

                try
                {
                    while (true)
                    {
                        // Get the block request from the `BlockManager` process.
                        sw_read?.Start();
                        var volume_id = await self.Input.ReadAsync().ConfigureAwait(false);
                        sw_read?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DownloadVolume", null, "Downloading volume {0}", volume_id);

                        // Trigger the download.
                        sw_wait?.Start();
                        TempFile f;
                        var (volume_name, size, hash) = await db
                            .GetVolumeInfo(volume_id, results.TaskControl.ProgressToken)
                            .FirstAsync()
                            .ConfigureAwait(false);
                        try
                        {
                            f = await backend.GetDirectAsync(volume_name, hash, size, results.TaskControl.TransferToken).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            lock (results)
                                results.BrokenRemoteFiles.Add(volume_name);

                            throw;
                        }
                        sw_wait?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DownloadVolume", null, "Downloaded volume {0} (ID: {1})", volume_name, volume_id);

                        // Pass the download handle (which may or may not have downloaded already) to the `VolumeDecryptor` process.
                        sw_write?.Start();
                        await self.Output.WriteAsync((volume_id, volume_name, f)).ConfigureAwait(false);
                        sw_write?.Stop();
                        Logging.Log.WriteExplicitMessage(LOGTAG, "DownloadVolume", null, "Passed volume {0} (ID: {1}) to next stage", volume_name, volume_id);
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume downloader retired");

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Read: {sw_read!.ElapsedMilliseconds}ms, Write: {sw_write!.ElapsedMilliseconds}ms, Wait: {sw_wait!.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "DownloadError", ex, "Error during download");
                    self.Input.Retire();
                    self.Output.Retire();
                    throw;
                }
            });
        }
    }

}