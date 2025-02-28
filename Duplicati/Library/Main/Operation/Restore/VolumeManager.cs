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

using CoCoL;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that manages the volumes that the `VolumeDownloader` process has downloaded.
    /// It is responsible for fetching the volumes from the backend and caching them.
    /// </summary>
    internal class VolumeManager
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeManager>();

        /// <summary>
        /// Runs the volume manager process.
        /// </summary>
        /// <param name="channels">The named channels for the restore operation.</param>
        /// <param name="options">The restore options.</param>
        public static Task Run(Channels channels, Options options)
        {
            return AutomationExtensions.RunTask(
                new
                {
                    VolumeRequestResponse = channels.VolumeRequestResponse.AsRead(),
                    DecompressRequest = channels.DecompressionRequest.AsWrite(),
                    DownloadRequest = channels.DownloadRequest.AsWrite(),
                },
                async self =>
                {
                    Dictionary<long, BlockVolumeReader> cache = [];
                    Dictionary<long, List<BlockRequest>> in_flight = [];

                    Stopwatch sw_cache_set = options.InternalProfiling ? new() : null;
                    Stopwatch sw_cache_evict = options.InternalProfiling ? new() : null;
                    Stopwatch sw_query = options.InternalProfiling ? new() : null;
                    Stopwatch sw_backend = options.InternalProfiling ? new() : null;
                    Stopwatch sw_request = options.InternalProfiling ? new() : null;
                    Stopwatch sw_wakeup = options.InternalProfiling ? new() : null;

                    try
                    {
                        while (true)
                        {
                            var msg = await self.VolumeRequestResponse.ReadAsync().ConfigureAwait(false);

                            switch (msg)
                            {
                                case BlockRequest request:
                                    {
                                        if (request.CacheDecrEvict)
                                        {
                                            sw_cache_evict?.Start();
                                            cache.Remove(request.VolumeID, out var reader);
                                            reader?.Dispose();
                                            sw_cache_evict?.Stop();
                                        }
                                        else
                                        {
                                            sw_request?.Start();
                                            if (cache.TryGetValue(request.VolumeID, out BlockVolumeReader reader))
                                            {
                                                await self.DecompressRequest.WriteAsync((request, reader)).ConfigureAwait(false);
                                            }
                                            else
                                            {
                                                if (in_flight.TryGetValue(request.VolumeID, out var waiters))
                                                {
                                                    waiters.Add(request);
                                                }
                                                else
                                                {
                                                    await self.DownloadRequest.WriteAsync(request.VolumeID).ConfigureAwait(false);
                                                    in_flight[request.VolumeID] = [request];
                                                }
                                            }
                                            sw_request?.Stop();
                                        }
                                        break;
                                    }
                                case (long volume_id, BlockVolumeReader reader):
                                    {
                                        sw_cache_set?.Start();
                                        cache[volume_id] = reader;
                                        sw_cache_set?.Stop();
                                        sw_wakeup?.Start();
                                        foreach (var request in in_flight[volume_id])
                                        {
                                            await self.DecompressRequest.WriteAsync((request, reader)).ConfigureAwait(false);
                                        }
                                        in_flight.Remove(volume_id);
                                        sw_wakeup?.Stop();
                                        break;
                                    }
                                default:
                                    throw new InvalidOperationException("Unexpected message type");
                            }
                        }
                    }
                    catch (RetiredException)
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume manager retired");

                        if (options.InternalProfiling)
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"CacheSet: {sw_cache_set.ElapsedMilliseconds}ms, CacheEvict: {sw_cache_evict.ElapsedMilliseconds}ms, Query: {sw_query.ElapsedMilliseconds}ms, Backend: {sw_backend.ElapsedMilliseconds}ms, Request: {sw_request.ElapsedMilliseconds}ms, Wakeup: {sw_wakeup.ElapsedMilliseconds}ms");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "VolumeManagerError", ex, "Error during volume manager");
                        throw;
                    }
                }
            );
        }
    }

}