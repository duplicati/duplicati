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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Database;
using Microsoft.Extensions.Caching.Memory;

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that manages the block requests and responses to/from the
    /// `FileProcessor` process by caching the blocks.
    /// </summary>
    internal class BlockManager
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BlockManager>();

        /// <summary>
        /// Dictionary for data blocks that are being cached. Whenever a block
        /// is requested, it checks if it is in the cache. If it is not, it
        /// will request the block from the corresponding volume. The requester
        /// will be given a `Task` that will be completed when the block is
        /// available. The cache will also keep track of how many times a block
        /// is requested, and only remove it from the cache when all requests
        /// have been fulfilled. This is to ensure that the cache is not
        /// prematurely evicted, while keeping the memory usage low. The
        /// dictionary also keeps track of how many readers are accessing it,
        /// and will retire the volume request channel when the last reader is
        /// done. Once disposed, the dictionary will clean up the database
        /// table that was used to keep track of the block counts.
        /// </summary>
        internal class SleepableDictionary : IDisposable
        {
            /// <summary>
            /// Channel for submitting block requests from a volume.
            /// </summary>
            private readonly IWriteChannel<object> m_volume_request;
            /// <summary>
            /// The dictionary holding the cached blocks.
            /// </summary>
            private readonly MemoryCache m_block_cache;
            /// <summary>
            /// The dictionary holding the `Task` for each block request in flight.
            /// </summary>
            private readonly ConcurrentDictionary<long, TaskCompletionSource<byte[]>> m_waiters = new();
            /// <summary>
            /// The number of readers accessing this dictionary. Used during shutdown / cleanup.
            /// </summary>
            private int readers = 0;
            /// <summary>
            /// Internal stopwatch for profiling the cache eviction.
            /// </summary>
            private readonly Stopwatch sw_cacheevict;
            /// <summary>
            /// Internal stopwatch for profiling the `CheckCounts` method.
            /// </summary>
            private readonly Stopwatch sw_checkcounts;
            /// <summary>
            /// Internal stopwatch for profiling setting up the waiters.
            /// </summary>
            private readonly Stopwatch sw_get_wait;
            /// <summary>
            /// Dictionary for keeping track of how many times each block is requested. Used to determine when a block is no longer needed.
            /// </summary>
            private readonly Dictionary<long, long> m_blockcount = new();
            /// <summary>
            /// Lock for the block count dictionary.
            /// </summary>
            private readonly object m_blockcount_lock = new();
            /// <summary>
            /// Dictionary for keeping track of how many times each volume is requested. Used to determine when a volume is no longer needed.
            /// </summary>
            private readonly Dictionary<long, long> m_volumecount = new();
            /// <summary>
            /// The options for the restore.
            /// </summary>
            private readonly Options m_options;
            /// <summary>
            /// The cache eviction options. Used for registering a callback when a block is evicted from the cache.
            /// </summary>
            private readonly MemoryCacheEntryOptions m_entry_options = new();
            /// <summary>
            /// Dictionary to keep track of how many active readers are accessing each block. On eviction, the byte[] buffer can only be returned to the ArrayPool if there are no active readers.
            /// </summary>
            private readonly ConcurrentDictionary<long, long> m_active_readers = [];

            /// <summary>
            /// Initializes a new instance of the <see cref="SleepableDictionary"/> class.
            /// </summary>
            /// <param name="db">The database holding information about how many of each block this restore requires.</param>
            /// <param name="volume_request">Channel for submitting block requests from a volume.</param>
            /// <param name="readers">Number of readers accessing this dictionary. Used during shutdown / cleanup.</param>
            public SleepableDictionary(LocalRestoreDatabase db, IWriteChannel<object> volume_request, Options options, int readers)
            {
                m_options = options;
                m_volume_request = volume_request;
                var cache_options = new MemoryCacheOptions();
                m_block_cache = new MemoryCache(cache_options);
                m_entry_options.RegisterPostEvictionCallback(async (key, value, reason, state) =>
                {
                    bool was_present = false;
                    while (true)
                    {
                        lock (m_blockcount_lock)
                        {
                            if (m_active_readers.TryGetValue((long)key, out var ac))
                            {
                                if (ac == 0)
                                {
                                    m_block_cache.Remove(key);
                                    was_present = true;
                                    break;
                                }
                            }
                            else
                                break;
                        }

                        await Task.Delay(10).ConfigureAwait(false);
                    }
                    if (was_present)
                        ArrayPool<byte>.Shared.Return((byte[])value);
                });
                this.readers = readers;
                sw_cacheevict = options.InternalProfiling ? new() : null;
                sw_checkcounts = options.InternalProfiling ? new() : null;
                sw_get_wait = options.InternalProfiling ? new() : null;

                foreach (var (block_id, volume_id) in db.GetBlocksAndVolumeIDs(options.SkipMetadata))
                {
                    var bc = m_blockcount.TryGetValue(block_id, out var c);
                    m_blockcount[block_id] = bc ? c + 1 : 1;
                    var vc = m_volumecount.TryGetValue(volume_id, out var v);
                    m_volumecount[volume_id] = vc ? v + 1 : 1;
                }
            }

            /// <summary>
            /// Decrements the block counters for the block and volume, and checks
            /// if the block is still needed. If the block is no longer needed, it
            /// will be removed from the cache. If the volume is no longer needed,
            /// the VolumeDownloader will be notified.
            /// </summary>
            /// <param name="blockRequest">The block request to check.</param>
            public async Task CheckCounts(BlockRequest blockRequest)
            {
                long error_block_id = -1;
                long error_volume_id = -1;
                var emit_evict = false;
                byte[] data = null;

                lock (m_blockcount_lock)
                {
                    sw_checkcounts?.Start();

                    var active = m_active_readers.TryGetValue(blockRequest.BlockID, out var ac) ? ac - 1 : 0;
                    if (active == 0)
                        m_active_readers.Remove(blockRequest.BlockID, out var _);
                    else
                        m_active_readers[blockRequest.BlockID] = active;

                    var block_count = m_blockcount.TryGetValue(blockRequest.BlockID, out var c) ? c - 1 : 0;

                    if (block_count > 0)
                    {
                        m_blockcount[blockRequest.BlockID] = block_count;
                    }
                    else if (block_count == 0)
                    {
                        // Evict the block from the cache and check if the volume is no longer needed.
                        m_blockcount.Remove(blockRequest.BlockID);
                        data = m_block_cache.Get<byte[]>(blockRequest.BlockID);
                        m_block_cache.Remove(blockRequest.BlockID);
                    }
                    else // block_count < 0
                    {
                        error_block_id = blockRequest.BlockID;
                    }

                    var vol_count = m_volumecount.TryGetValue(blockRequest.VolumeID, out var vc) ? vc - 1 : 0;
                    if (vol_count > 0)
                    {
                        m_volumecount[blockRequest.VolumeID] = vol_count;
                    }
                    else if (vol_count == 0)
                    {
                        m_volumecount.Remove(blockRequest.VolumeID);
                        blockRequest.CacheDecrEvict = true;
                        emit_evict = true;
                    }
                    else // vol_count < 0
                    {
                        error_volume_id = blockRequest.VolumeID;
                    }
                    sw_checkcounts?.Stop();
                }

                if (data != null)
                    ArrayPool<byte>.Shared.Return(data);

                // Notify the `VolumeManager` that it should evict the volume.
                if (emit_evict)
                    await m_volume_request.WriteAsync(blockRequest).ConfigureAwait(false);

                if (error_block_id != -1)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BlockCountError", null, $"Block {blockRequest.BlockID} has a count below 0");
                }

                if (error_volume_id != -1)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "VolumeCountError", null, $"Volume {blockRequest.VolumeID} has a count below 0");
                }
            }

            /// <summary>
            /// Get a block from the cache. If the block is not in the cache,
            /// it will request the block from the volume and return a `Task`
            /// that will be completed when the block is available.
            /// </summary>
            /// <param name="block_request">The requested block.</param>
            /// <returns>A `Task` holding the data block.</returns>
            public Task<byte[]> Get(BlockRequest block_request)
            {
                lock (m_blockcount_lock)
                {
                    m_active_readers[block_request.BlockID] = m_active_readers.TryGetValue(block_request.BlockID, out var c) ? c + 1 : 1;
                }

                // Check if the block is already in the cache, and return it if it is.
                if (m_block_cache != null && m_block_cache.TryGetValue(block_request.BlockID, out byte[] value))
                {
                    return Task.FromResult(value);
                }

                // If the block is not in the cache, request it from the volume.
                sw_get_wait?.Start();
                var tcs = new TaskCompletionSource<byte[]>();
                var new_tcs = m_waiters.GetOrAdd(block_request.BlockID, tcs);
                if (tcs == new_tcs)
                {
                    // We are the first to request this block
                    m_volume_request.Write(block_request);
                }
                sw_get_wait?.Stop();

                return new_tcs.Task;
            }

            /// <summary>
            /// Set a block in the cache. If the block is already in the cache,
            /// it will be replaced. If the block is not in the cache, it will
            /// be added. If the block is no longer needed, it will be removed
            /// from the cache. If the block is requested while it is being
            /// removed, the requester will be given a `Task` that will be
            /// completed when the block is available.
            /// </summary>
            /// <param name="blockRequest">The block request related to the value.</param>
            /// <param name="value">The byte[] buffer holding the block data.</param>
            public void Set(BlockRequest blockRequest, byte[] value)
            {
                m_block_cache.Set(blockRequest.BlockID, value);

                // Notify any waiters that the block is available.
                if (m_waiters.TryRemove(blockRequest.BlockID, out var tcs))
                {
                    tcs.SetResult(value);
                }

                sw_cacheevict?.Start();
                if (m_block_cache.Count > m_options.RestoreCacheMax)
                {
                    m_block_cache.Compact(m_options.RestoreCacheMax == 0 ? 1.0 : m_options.RestoreCacheEvict);
                }
                sw_cacheevict?.Stop();
            }

            /// <summary>
            /// Retire the dictionary. This will decrement the number of readers
            /// accessing the dictionary, and if there are no more readers, it
            /// will retire the volume request channel, effectively shutting
            /// down the restore process network.
            /// </summary>
            public void Retire()
            {
                if (Interlocked.Decrement(ref readers) <= 0)
                {
                    m_volume_request.Retire();
                }
            }

            /// <summary>
            /// Clean up the dictionary. This will remove the database table
            /// that was used to keep track of the block counts.
            /// </summary>
            public void Dispose()
            {
                // Verify that the tables are empty
                var blockcount = m_blockcount.Sum(x => x.Value);
                var volumecount = m_volumecount.Sum(x => x.Value);

                if (blockcount != 0)
                {
                    var blocks = m_blockcount.Where(x => x.Value != 0).Select(x => x.Key).ToArray();
                    var blockids = string.Join(", ", blocks);
                    Logging.Log.WriteWarningMessage(LOGTAG, "BlockCountError", null, $"Block count in SleepableDictionarys block table is not zero: {blockcount}{Environment.NewLine}");
                }

                if (m_active_readers.Count > 0)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BlockCountError", null, $"There are still {m_active_readers.Count} files being read by {m_active_readers.Sum(x => x.Value)} readers");
                }

                if (volumecount != 0)
                {
                    var vols = m_volumecount.Where(x => x.Value != 0).Select(x => x.Key).ToArray();
                    var volids = string.Join(", ", vols);
                    Logging.Log.WriteWarningMessage(LOGTAG, "VolumeCountError", null, $"Volume count in SleepableDictionarys volume table is not zero: {volumecount}{Environment.NewLine}Volumes: {volids}");
                }

                if (m_options.InternalProfiling)
                {
                    Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Sleepable dictionary - CheckCounts: {sw_checkcounts.ElapsedMilliseconds}ms, Get wait: {sw_get_wait.ElapsedMilliseconds}ms, Cache evict: {sw_cacheevict.ElapsedMilliseconds}ms");
                }

                if (m_block_cache.Count > 0)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BlockCacheMismatch", null, $"Internal Block cache is not empty: {m_block_cache.Count}");
                    Logging.Log.WriteWarningMessage(LOGTAG, "BlockCacheMismatch", null, $"Block counts in cache ({m_blockcount.Count}): {string.Join(", ", m_blockcount.Select(x => x.Value))}");
                }
            }

            /// <summary>
            /// Cancel all pending requests. This will set an exception on all
            /// pending requests, effectively cancelling them.
            /// </summary>
            public void CancelAll()
            {
                foreach (var tcs in m_waiters.Values)
                {
                    tcs.SetException(new RetiredException("Request waiter"));
                }
            }
        }

        /// <summary>
        /// Run the block manager process. This will create a cache for the
        /// blocks, and start two tasks: one for reading blocks from the input
        /// channel (data blocks from the volumes) and storing them in the
        /// cache, and one for reading block requests from the `FileProcessor`,
        /// accessing the cache for the blocks, and writing the resulting
        /// blocks back to the `FileProcessor`.
        /// </summary>
        /// <param name="channels">The named channels for the restore operation.</param>
        /// <param name="db">The database holding information about how many of each block this restore requires.</param>
        /// <param name="options">The restore options.</param>
        /// <param name="fp_requests">The channels for reading block requests from the `FileProcessor`.</param>
        /// <param name="fp_responses">The channels for writing block responses back to the `FileProcessor`.</param>
        public static Task Run(Channels channels, LocalRestoreDatabase db, Options options, IChannel<BlockRequest>[] fp_requests, IChannel<byte[]>[] fp_responses)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.DecompressedBlock.AsRead(),
                Output = channels.VolumeRequestResponse.AsWrite()
            },
            async self =>
            {
                // Create a cache for the blocks,
                using SleepableDictionary cache = new(db, self.Output, options, fp_requests.Length);

                // The volume consumer will read blocks from the input channel (data blocks from the volumes) and store them in the cache.
                var volume_consumer = Task.Run(async () =>
                {
                    Stopwatch sw_read = options.InternalProfiling ? new() : null;
                    Stopwatch sw_set = options.InternalProfiling ? new() : null;
                    try
                    {
                        while (true)
                        {
                            sw_read?.Start();
                            var (block_request, data) = await self.Input.ReadAsync().ConfigureAwait(false);
                            sw_read?.Stop();

                            sw_set?.Start();
                            cache.Set(block_request, data);
                            sw_set?.Stop();
                        }
                    }
                    catch (RetiredException)
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "BlockManager Volume consumer retired");

                        if (options.InternalProfiling)
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Volume consumer - Read: {sw_read.ElapsedMilliseconds}ms, Set: {sw_set.ElapsedMilliseconds}ms");
                        }

                        // Cancel any remaining readers - although there shouldn't be any.
                        cache.CancelAll();
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "VolumeConsumerError", ex, "Error in volume consumer");

                        // Cancel any remaining readers - although there shouldn't be any.
                        cache.CancelAll();
                    }
                });

                // The block handlers will read block requests from the `FileProcessor`, access the cache for the blocks, and write the resulting blocks to the `FileProcessor`.
                var block_handlers = fp_requests.Zip(fp_responses, (req, res) => Task.Run(async () =>
                {
                    Stopwatch sw_req = options.InternalProfiling ? new() : null;
                    Stopwatch sw_resp = options.InternalProfiling ? new() : null;
                    Stopwatch sw_cache = options.InternalProfiling ? new() : null;
                    Stopwatch sw_get = options.InternalProfiling ? new() : null;
                    try
                    {
                        while (true)
                        {
                            sw_req?.Start();
                            var block_request = await req.ReadAsync().ConfigureAwait(false);
                            sw_req?.Stop();
                            if (block_request.CacheDecrEvict)
                            {
                                sw_cache?.Start();
                                // Target file already had the block.
                                await cache.CheckCounts(block_request).ConfigureAwait(false);
                                sw_cache?.Stop();
                            }
                            else
                            {
                                sw_get?.Start();
                                var data = await cache.Get(block_request).ConfigureAwait(false);
                                sw_get?.Stop();

                                sw_resp?.Start();
                                await res.WriteAsync(data).ConfigureAwait(false);
                                sw_resp?.Stop();
                            }
                        }
                    }
                    catch (RetiredException)
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "BlockManager Block handler retired");

                        if (options.InternalProfiling)
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Block handler - Req: {sw_req.ElapsedMilliseconds}ms, Resp: {sw_resp.ElapsedMilliseconds}ms, Cache: {sw_cache.ElapsedMilliseconds}ms, Get: {sw_get.ElapsedMilliseconds}ms");
                        }

                        cache.Retire();
                    }
                })).ToArray();

                await Task.WhenAll([volume_consumer, .. block_handlers]).ConfigureAwait(false);
            });
        }
    }

}
