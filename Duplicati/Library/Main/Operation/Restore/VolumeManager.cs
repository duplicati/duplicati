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
        /// Helper class to read from either of two channels.
        /// This is a workaround for the fact that CoCoL seems to deadlock on ReadFroAnyAsync
        /// </summary>
        /// <param name="channel1">The first channel to read from.</param>
        /// <param name="channel2">The second channel to read from.</param>
        private sealed class ReadFromEither(IReadChannel<object> channel1, IReadChannel<object> channel2)
        {
            /// <summary>
            /// The first task that is reading from the channels.
            /// </summary>
            private Task<object> t1;
            /// <summary>
            /// The second task that is reading from the channels.
            /// </summary>
            private Task<object> t2;

            /// <summary>
            /// Reads from either of the two channels asynchronously.
            /// </summary>
            /// <returns>The object read from the channel.</returns>
            public async Task<object> ReadFromEitherAsync()
            {
                // NOTE: This is not a correct external choice,
                // as we have actually consumed from both channels,
                // but we only process one of them
                // This is safe here, because the shutdown only happens on failure termination
                t1 ??= channel1.ReadAsync();
                t2 ??= channel2.ReadAsync();

                var r = await Task.WhenAny(t1, t2).ConfigureAwait(false);
                if (r == t1)
                {
                    t1 = null;
                    return await r.ConfigureAwait(false);
                }
                else
                {
                    t2 = null;
                    return await r.ConfigureAwait(false);
                }
            }
        }

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
                    VolumeRequest = channels.VolumeRequest.AsRead(),
                    VolumeResponse = channels.VolumeResponse.AsRead(),
                    DecompressRequest = channels.DecompressionRequest.AsWrite(),
                    DecompressAck = channels.DecompressionAck.AsRead(),
                    DownloadRequest = channels.DownloadRequest.AsWrite(),
                },
                async self =>
                {
                    // The maximum number of volumes to have in cache at once.
                    long cache_max = options.RestoreVolumeCacheMax / options.VolumeSize;
                    // Cache of volume readers.
                    Dictionary<long, BlockVolumeReader> cache = [];
                    // List of which volume was accessed last. Used for cache eviction.
                    List<long> cache_last_touched = [];
                    // Cache of volumes.
                    Dictionary<long, TempFile> tmpfiles = [];
                    // Dictionary to keep track of active downloads. Used for grouping requests to the same volume.
                    Dictionary<long, List<BlockRequest>> in_flight_downloads = [];
                    // Dictionary to keep track of volumes that are actively being accessed. Used for cache eviction.
                    Dictionary<long, long> in_flight_decompressing = [];

                    Stopwatch sw_cache_set = options.InternalProfiling ? new() : null;
                    Stopwatch sw_cache_evict = options.InternalProfiling ? new() : null;
                    Stopwatch sw_query = options.InternalProfiling ? new() : null;
                    Stopwatch sw_backend = options.InternalProfiling ? new() : null;
                    Stopwatch sw_request = options.InternalProfiling ? new() : null;
                    Stopwatch sw_wakeup = options.InternalProfiling ? new() : null;

                    async Task flush_ack_channel()
                    {
                        // Check if we need to flush the ack channel
                        while (true)
                        {
                            var (has_read, ack_msg) = await self.DecompressAck.TryReadAsync().ConfigureAwait(false);
                            var ack_request = ack_msg as BlockRequest;
                            if (!has_read)
                                break;
                            Logging.Log.WriteVerboseMessage(LOGTAG, "VolumeRequest", "Decompression acknowledgment for block {0} from volume {1}", ack_request.BlockID, ack_request.VolumeID);
                            if (in_flight_decompressing.TryGetValue(ack_request.VolumeID, out var dcount))
                            {
                                if (dcount <= 1)
                                    in_flight_decompressing.Remove(ack_request.VolumeID);
                                else
                                    in_flight_decompressing[ack_request.VolumeID] = dcount - 1;
                            }
                            else
                                Logging.Log.WriteWarningMessage(LOGTAG, "VolumeRequest", null, "Decompression acknowledgment for block {0} from volume {1} not found", ack_request.BlockID, ack_request.VolumeID);
                        }
                    }


                    var rfa = new ReadFromEither(self.VolumeResponse, self.VolumeRequest);
                    try
                    {
                        while (true)
                        {
                            await flush_ack_channel().ConfigureAwait(false);
                            // TODO: CoCol ReadFromAnyAsync deadlocks, so we use a workaround
                            var msg = await rfa.ReadFromEitherAsync().ConfigureAwait(false);
                            switch (msg)
                            {
                                case BlockRequest request:
                                    switch (request.RequestType)
                                    {
                                        case BlockRequestType.CacheEvict:
                                            {
                                                Logging.Log.WriteExplicitMessage(LOGTAG, "VolumeRequest", "Evicting volume {0} from cache by request", request.VolumeID);
                                                sw_cache_evict?.Start();
                                                cache.Remove(request.VolumeID, out var reader);
                                                cache_last_touched.Remove(request.VolumeID);
                                                reader?.Dispose();
                                                tmpfiles.Remove(request.VolumeID, out var tmpfile);
                                                tmpfile?.Dispose();
                                                sw_cache_evict?.Stop();
                                            }
                                            break;
                                        case BlockRequestType.DecompressAck:
                                            {
                                                Logging.Log.WriteExplicitMessage(LOGTAG, "VolumeRequest", "Decompression acknowledgment for block {0} from volume {1}", request.BlockID, request.VolumeID);
                                                if (in_flight_decompressing.TryGetValue(request.VolumeID, out var count))
                                                {
                                                    if (count <= 1)
                                                        in_flight_decompressing.Remove(request.VolumeID);
                                                    else
                                                        in_flight_decompressing[request.VolumeID] = count - 1;
                                                }
                                                else
                                                    Logging.Log.WriteWarningMessage(LOGTAG, "VolumeRequest", null, "Decompression acknowledgment for block {0} from volume {1} not found", request.BlockID, request.VolumeID);
                                            }
                                            break;
                                        case BlockRequestType.Download:
                                            {
                                                Logging.Log.WriteExplicitMessage(LOGTAG, "VolumeRequest", "Got a request for block {0} from volume {1}", request.BlockID, request.VolumeID);
                                                sw_request?.Start();
                                                if (cache.TryGetValue(request.VolumeID, out BlockVolumeReader reader))
                                                {
                                                    cache_last_touched.Remove(request.VolumeID);
                                                    cache_last_touched.Add(request.VolumeID);
                                                    Logging.Log.WriteExplicitMessage(LOGTAG, "VolumeRequest", "Block {0} found in cache", request.BlockID);
                                                    await self.DecompressRequest.WriteAsync((request, reader)).ConfigureAwait(false);
                                                    Logging.Log.WriteExplicitMessage(LOGTAG, "VolumeRequest", "Requesting decompression of block {0} from cached volume {1}", request.BlockID, request.VolumeID);
                                                    if (in_flight_decompressing.TryGetValue(request.VolumeID, out var count))
                                                        in_flight_decompressing[request.VolumeID] = count + 1;
                                                    else
                                                        in_flight_decompressing[request.VolumeID] = 1;
                                                }
                                                else
                                                {
                                                    // Check if downloading another volume would exceed cache limits.
                                                    if ((cache.Count + in_flight_downloads.Count) >= cache_max)
                                                    {
                                                        Logging.Log.WriteWarningMessage(LOGTAG, "VolumeRequest", null, "Evicting volume");
                                                        // TODO switch based of the eviction strategy.
                                                        // fifo / lifo based on both when they were downloaded and when they were used
                                                        // random
                                                        // Heuristic based of accesses and recency
                                                        // Cache would overflow if we request another; we have to evict something, or store the request for later.

                                                        // LRU
                                                        for (int i = 0; i < cache_last_touched.Count; i++)
                                                        {
                                                            var volume_id = cache_last_touched[i];
                                                            if (!(in_flight_decompressing.TryGetValue(volume_id, out var count) && count > 0))
                                                            {
                                                                // Entry can be safely evicted
                                                                cache.Remove(volume_id);
                                                                cache_last_touched.RemoveAt(i);
                                                                tmpfiles.Remove(volume_id);
                                                                break;
                                                            }
                                                        }
                                                    }

                                                    Logging.Log.WriteExplicitMessage(LOGTAG, "VolumeRequest", "Block {0} not found in cache, requesting volume {1}", request.BlockID, request.VolumeID);
                                                    if (in_flight_downloads.TryGetValue(request.VolumeID, out var waiters))
                                                    {
                                                        waiters.Add(request);
                                                    }
                                                    else
                                                    {
                                                        await self.DownloadRequest.WriteAsync(request.VolumeID).ConfigureAwait(false);
                                                        in_flight_downloads[request.VolumeID] = [request];
                                                    }
                                                }
                                                sw_request?.Stop();
                                            }
                                            break;
                                        default:
                                            throw new InvalidOperationException($"Unexpected request type: {request.RequestType}");
                                    }
                                    break;
                                case (long volume_id, TempFile tmpfile, BlockVolumeReader reader):
                                    {
                                        sw_cache_set?.Start();
                                        cache[volume_id] = reader;
                                        cache_last_touched.Add(volume_id);
                                        tmpfiles[volume_id] = tmpfile;
                                        sw_cache_set?.Stop();
                                        sw_wakeup?.Start();
                                        foreach (var request in in_flight_downloads[volume_id])
                                        {
                                            // Ensure ACK channel is empty to avoid deadlock
                                            await flush_ack_channel().ConfigureAwait(false);

                                            // Request the decompressions
                                            Logging.Log.WriteExplicitMessage(LOGTAG, "VolumeRequest", "Requesting block {0} from newly cached volume {1}", request.BlockID, volume_id);
                                            await self.DecompressRequest.WriteAsync((request, reader)).ConfigureAwait(false);
                                            if (in_flight_decompressing.TryGetValue(request.VolumeID, out var count))
                                                in_flight_decompressing[request.VolumeID] = count + 1;
                                            else
                                                in_flight_decompressing[request.VolumeID] = 1;
                                        }
                                        in_flight_downloads.Remove(volume_id);
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