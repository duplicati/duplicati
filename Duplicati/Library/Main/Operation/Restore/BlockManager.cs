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

#nullable enable

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
            /// Secondary counter for the number of blocks in the cache. It's faster than the MemoryCache.Count property.
            /// </summary>
            private int m_block_cache_count = 0;
            /// <summary>
            /// Flag indicating if the cache is currently being compacted. Used to avoid triggering multiple compactions as they are expensive.
            /// Multiple triggers can occur as the eviction callback is launched in a task, so the count can be above the max for a short while.
            /// </summary>
            private int m_block_cache_compacting = 0;
            /// <summary>
            /// The dictionary holding the `Task` for each block request in flight.
            /// </summary>
            private readonly ConcurrentDictionary<long, (int Count, TaskCompletionSource<DataBlock> Task)> m_waiters = [];
            /// <summary>
            /// The number of readers accessing this dictionary. Used during shutdown / cleanup.
            /// </summary>
            private int m_readers = 0;
            /// <summary>
            /// Internal stopwatch for profiling the cache eviction.
            /// </summary>
            private readonly Stopwatch? sw_cacheevict;
            /// <summary>
            /// Internal stopwatch for profiling the `CheckCounts` method.
            /// </summary>
            private readonly Stopwatch? sw_checkcounts;
            /// <summary>
            /// Internal stopwatch for profiling setting up the waiters.
            /// </summary>
            private readonly Stopwatch? sw_get_wait;
            /// <summary>
            /// Internal stopwatch for profiling setting a block in the cache.
            /// </summary>
            private readonly Stopwatch? sw_set_set;
            /// <summary>
            /// Internal stopwatch for profiling getting a waiter to notify that the block is available.
            /// </summary>
            private readonly Stopwatch? sw_set_wake_get;
            /// <summary>
            /// Internal stopwatch for profiling for waking up the waiting request.
            /// </summary>
            private readonly Stopwatch? sw_set_wake_set;

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
            private bool m_retired = false;

            /// <summary>
            /// Initializes a new instance of the <see cref="SleepableDictionary"/> class.
            /// </summary>
            /// <param name="volume_request">Channel for submitting block requests from a volume.</param>
            /// <param name="readers">Number of readers accessing this dictionary. Used during shutdown / cleanup.</param>
            private SleepableDictionary(IWriteChannel<object> volume_request, Options options, int readers)
            {
                m_options = options;
                m_volume_request = volume_request;
                var cache_options = new MemoryCacheOptions();
                m_block_cache = new MemoryCache(cache_options);
                m_entry_options.RegisterPostEvictionCallback((key, value, reason, state) =>
                {
                    if (value is DataBlock dataBlock)
                    {
                        dataBlock.Dispose();
                    }
                    else if (value is null)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "CacheEvictCallback", null, "Evicted block {0} from cache, but the value was null", key);
                    }
                    else
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "CacheEvictCallback", null, "Evicted block {0} from cache, but the value was of unexpected type {1}", key, value.GetType().FullName);
                    }
                    Interlocked.Decrement(ref m_block_cache_count);
                    Logging.Log.WriteExplicitMessage(LOGTAG, "CacheEvictCallback", "Evicted block {0} from cache", key);
                    if (reason is EvictionReason.Capacity)
                        Interlocked.Exchange(ref m_block_cache_compacting, 0);
                });
                m_readers = readers;
                sw_cacheevict = options.InternalProfiling ? new() : null;
                sw_checkcounts = options.InternalProfiling ? new() : null;
                sw_get_wait = options.InternalProfiling ? new() : null;
                sw_set_set = options.InternalProfiling ? new() : null;
                sw_set_wake_get = options.InternalProfiling ? new() : null;
                sw_set_wake_set = options.InternalProfiling ? new() : null;
            }

            /// <summary>
            /// Asynchronously creates a new instance of the <see cref="SleepableDictionary"/> class.
            /// This method initializes the block and volume counts based on the data in the database.
            /// </summary>
            /// <param name="db">The database holding information about how many of each block this restore requires.</param>
            /// <param name="volume_request">CoCoL channel for submitting block requests from a volume.</param>
            /// <param name="options">The restore options.</param>
            /// <param name="readers">The number of readers accessing this dictionary. Used during shutdown / cleanup.</param>
            /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
            /// <returns>A task that when awaited returns a new instance of the <see cref="SleepableDictionary"/> class.</returns>
            public static async Task<SleepableDictionary> CreateAsync(LocalRestoreDatabase db, IWriteChannel<object> volume_request, Options options, int readers, CancellationToken cancellationToken)
            {
                var sd = new SleepableDictionary(volume_request, options, readers);

                await foreach (var (block_id, volume_id) in db.GetBlocksAndVolumeIDs(options.SkipMetadata, cancellationToken).ConfigureAwait(false))
                {
                    var bc = sd.m_blockcount.TryGetValue(block_id, out var c);
                    sd.m_blockcount[block_id] = bc ? c + 1 : 1;
                    var vc = sd.m_volumecount.TryGetValue(volume_id, out var v);
                    sd.m_volumecount[volume_id] = vc ? v + 1 : 1;
                }

                return sd;
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

                Logging.Log.WriteExplicitMessage(LOGTAG, "CheckCounts", "Trying to acquire m_blockcount_lock for block {0}", blockRequest.BlockID);
                lock (m_blockcount_lock)
                {
                    sw_checkcounts?.Start();

                    var block_count = m_blockcount.TryGetValue(blockRequest.BlockID, out var c) ? c - 1 : 0;

                    if (block_count > 0)
                    {
                        m_blockcount[blockRequest.BlockID] = block_count;
                    }
                    else if (block_count == 0)
                    {
                        // Evict the block from the cache and check if the volume is no longer needed.
                        m_blockcount.Remove(blockRequest.BlockID);
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
                        blockRequest.RequestType = BlockRequestType.CacheEvict;
                        emit_evict = true;
                    }
                    else // vol_count < 0
                    {
                        error_volume_id = blockRequest.VolumeID;
                    }
                    sw_checkcounts?.Stop();
                }
                Logging.Log.WriteExplicitMessage(LOGTAG, "CheckCounts", "Released m_blockcount_lock for block {0}", blockRequest.BlockID);

                // Notify the `VolumeManager` that it should evict the volume.
                if (emit_evict)
                    await m_volume_request.WriteAsync(blockRequest).ConfigureAwait(false);

                if (error_block_id != -1)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BlockCountError", null, "Block {0} has a count below 0", blockRequest.BlockID);
                }

                if (error_volume_id != -1)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "VolumeCountError", null, "Volume {0} has a count below 0", blockRequest.VolumeID);
                }
            }

            /// <summary>
            /// Get a block from the cache. If the block is not in the cache,
            /// it will request the block from the volume and return a `Task`
            /// that will be completed when the block is available.
            /// </summary>
            /// <param name="block_request">The requested block.</param>
            /// <returns>A `Task` holding the data block.</returns>
            public async Task<DataBlock> Get(BlockRequest block_request)
            {
                Logging.Log.WriteExplicitMessage(LOGTAG, "BlockCacheGet", "Getting block {0} from cache", block_request.BlockID);

                // Check if the block is already in the cache, and return it if it is.
                if (m_block_cache.TryGetValue(block_request.BlockID, out DataBlock? value))
                {
                    if (value is null)
                        throw new InvalidOperationException($"Block {block_request.BlockID} was in the cache, but the value was null");

                    value.Reference();

                    // If the block was evicted in between the TryGetValue and the Reference call,
                    // we need to request it again.
                    if (value.Data is not null)
                        return value;
                }

                // If the block is not in the cache, request it from the volume.
                sw_get_wait?.Start();
                var tcs = new TaskCompletionSource<DataBlock>(TaskCreationOptions.RunContinuationsAsynchronously);
                var (_, new_tcs) = m_waiters.AddOrUpdate(block_request.BlockID, (1, tcs), (key, old) => (old.Count + 1, old.Task));
                if (tcs == new_tcs)
                {
                    Logging.Log.WriteExplicitMessage(LOGTAG, "BlockCacheGet", "Requesting block {0} from volume {1}", block_request.BlockID, block_request.VolumeID);

                    // We are the first to request this block
                    await m_volume_request.WriteAsync(block_request).ConfigureAwait(false);

                    sw_get_wait?.Stop();

                    // Add a timeout monitor
                    using var tcs1 = new CancellationTokenSource();
                    var t = await Task.WhenAny(Task.Delay(TimeSpan.FromMinutes(5), tcs1.Token), new_tcs.Task).ConfigureAwait(false);
                    if (t != new_tcs.Task)
                        Logging.Log.WriteWarningMessage(LOGTAG, "BlockRequestTimeout", null, "Block request for block {0} has been in flight for over 5 minutes. This may be a deadlock.", block_request.BlockID);
                }
                else
                {
                    Logging.Log.WriteExplicitMessage(LOGTAG, "BlockCacheGet", "Block {0} is already being requested, waiting for it to be available", block_request.BlockID);
                    sw_get_wait?.Stop();
                }

                return await new_tcs.Task.ConfigureAwait(false);
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
            public void Set(long blockID, byte[] value)
            {
                // TODO If this block is only needed once, then it can skip the cache.
                Logging.Log.WriteExplicitMessage(LOGTAG, "BlockCacheSet", "Setting block {0} in cache", blockID);

                sw_set_set?.Start();
                var block = new DataBlock(value); // Implicitly referenced on creation.
                m_block_cache.Set(blockID, block, m_entry_options);
                Interlocked.Increment(ref m_block_cache_count);
                sw_set_set?.Stop();

                // Notify any waiters that the block is available.
                sw_set_wake_get?.Start();
                if (m_waiters.TryRemove(blockID, out var entry))
                {
                    sw_set_wake_get?.Stop();
                    sw_set_wake_set?.Start();
                    block.Reference(entry.Count);
                    entry.Task.SetResult(block);
                    sw_set_wake_set?.Stop();
                }
                sw_set_wake_get?.Stop();

                sw_cacheevict?.Start();
                if (m_block_cache_count > m_options.RestoreCacheMax && Interlocked.CompareExchange(ref m_block_cache_compacting, 1, 0) == 0)
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
                if (!m_retired && Interlocked.Decrement(ref m_readers) <= 0)
                {
                    m_volume_request.Retire();
                    m_retired = true;
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
                    var blocks = m_blockcount
                        .Where(x => x.Value != 0)
                        .Take(10)
                        .Select(x => x.Key);
                    var blockids = string.Join(", ", blocks);
                    Logging.Log.WriteErrorMessage(LOGTAG, "BlockCountError", null, $"Block count in SleepableDictionarys block table is not zero: {blockcount}{Environment.NewLine}First 10 blocks: {blockids}");
                }

                if (volumecount != 0)
                {
                    var vols = m_volumecount
                        .Where(x => x.Value != 0)
                        .Take(10)
                        .Select(x => x.Key);
                    var volids = string.Join(", ", vols);
                    Logging.Log.WriteErrorMessage(LOGTAG, "VolumeCountError", null, $"Volume count in SleepableDictionarys volume table is not zero: {volumecount}{Environment.NewLine}First 10 volumes: {volids}");
                }

                if (m_block_cache.Count > 0)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "BlockCacheMismatch", null, $"Internal Block cache is not empty: {m_block_cache.Count}");
                    Logging.Log.WriteErrorMessage(LOGTAG, "BlockCacheMismatch", null, $"First 10 block counts in cache ({m_blockcount.Count}): {string.Join(", ", m_blockcount.Take(10).Select(x => x.Value))}");
                }

                if (m_options.InternalProfiling)
                {
                    Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Sleepable dictionary - CheckCounts: {sw_checkcounts!.ElapsedMilliseconds}ms, Get wait: {sw_get_wait!.ElapsedMilliseconds}ms, Set set: {sw_set_set!.ElapsedMilliseconds}ms, Set wake get: {sw_set_wake_get!.ElapsedMilliseconds}ms, Set wake set: {sw_set_wake_set!.ElapsedMilliseconds}ms, Cache evict: {sw_cacheevict!.ElapsedMilliseconds}ms");
                }

                if (!m_retired)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "NotRetired", null, "SleepableDictionary was disposed without having retired channels.");
                    m_retired = true;
                    m_volume_request.Retire();
                }
            }

            /// <summary>
            /// Cancel all pending requests. This will set an exception on all
            /// pending requests, effectively cancelling them.
            /// </summary>
            public void CancelAll()
            {
                foreach (var (_, tcs) in m_waiters.Values)
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
        /// <param name="fp_requests">The channels for reading block requests from the `FileProcessor`.</param>
        /// <param name="fp_responses">The channels for writing block responses back to the `FileProcessor`.</param>
        /// <param name="options">The restore options.</param>
        /// <param name="results">The results of the restore operation.</param>
        public static Task Run(Channels channels, LocalRestoreDatabase db, IChannel<BlockRequest>[] fp_requests, IChannel<Task<DataBlock>>[] fp_responses, Options options, RestoreResults results)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.DecompressedBlock.AsRead(),
                Ack = channels.DecompressionAck.AsWrite(),
                Output = channels.VolumeRequest.AsWrite()
            },
            async self =>
            {
                // Create a cache for the blocks,
                using SleepableDictionary cache =
                    await SleepableDictionary.CreateAsync(db, self.Output, options, fp_requests.Length, results.TaskControl.ProgressToken)
                        .ConfigureAwait(false);

                // The volume consumer will read blocks from the input channel (data blocks from the volumes) and store them in the cache.
                var volume_consumer = Task.Run(async () =>
                {
                    Stopwatch? sw_read = options.InternalProfiling ? new() : null;
                    Stopwatch? sw_set = options.InternalProfiling ? new() : null;
                    try
                    {
                        while (true)
                        {
                            Logging.Log.WriteExplicitMessage(LOGTAG, "VolumeConsumer", null, "Waiting for block request from volume");
                            sw_read?.Start();
                            var (block_request, data) = await self.Input.ReadAsync().ConfigureAwait(false);
                            sw_read?.Stop();

                            Logging.Log.WriteExplicitMessage(LOGTAG, "VolumeConsumer", null, "Received data for block {0} from volume {1}", block_request.BlockID, block_request.VolumeID);
                            sw_set?.Start();
                            cache.Set(block_request.BlockID, data);
                            sw_set?.Stop();
                        }
                    }
                    catch (RetiredException)
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "BlockManager Volume consumer retired");

                        if (options.InternalProfiling)
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Volume consumer - Read: {sw_read!.ElapsedMilliseconds}ms, Set: {sw_set!.ElapsedMilliseconds}ms");
                        }

                        // Cancel any remaining readers - although there shouldn't be any.
                        cache.CancelAll();
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "VolumeConsumerError", ex, "Error in volume consumer");
                        self.Input.Retire();

                        // Cancel any remaining readers - although there shouldn't be any.
                        cache.CancelAll();
                        cache.Retire();
                    }
                });

                // The block handlers will read block requests from the `FileProcessor`, access the cache for the blocks, and write the resulting blocks to the `FileProcessor`.
                var block_handlers = fp_requests.Zip(fp_responses, (req, res) => Task.Run(async () =>
                {
                    Stopwatch? sw_req = options.InternalProfiling ? new() : null;
                    Stopwatch? sw_resp = options.InternalProfiling ? new() : null;
                    Stopwatch? sw_cache = options.InternalProfiling ? new() : null;
                    Stopwatch? sw_get = options.InternalProfiling ? new() : null;
                    try
                    {
                        while (true)
                        {
                            sw_req?.Start();
                            var block_request = await req.ReadAsync().ConfigureAwait(false);
                            sw_req?.Stop();
                            Logging.Log.WriteExplicitMessage(LOGTAG, "BlockHandler", null, "Received block request: {0}", block_request.RequestType);
                            switch (block_request.RequestType)
                            {
                                case BlockRequestType.Download:
                                    sw_get?.Start();
                                    var datatask = cache.Get(block_request);
                                    sw_get?.Stop();
                                    Logging.Log.WriteExplicitMessage(LOGTAG, "BlockHandler", null, "Retrieved data for block {0} and volume {1}", block_request.BlockID, block_request.VolumeID);

                                    sw_resp?.Start();
                                    await res.WriteAsync(datatask).ConfigureAwait(false);
                                    sw_resp?.Stop();
                                    Logging.Log.WriteExplicitMessage(LOGTAG, "BlockHandler", null, "Passed data for block {0} and volume {1} to FileProcessor", block_request.BlockID, block_request.VolumeID);
                                    break;
                                case BlockRequestType.CacheEvict:
                                    sw_cache?.Start();
                                    // Target file already had the block.
                                    await cache.CheckCounts(block_request).ConfigureAwait(false);
                                    sw_cache?.Stop();

                                    Logging.Log.WriteExplicitMessage(LOGTAG, "BlockHandler", null, "Decremented counts for block {0} and volume {1}", block_request.BlockID, block_request.VolumeID);
                                    break;
                                default:
                                    throw new InvalidOperationException($"Unexpected block request type: {block_request.RequestType}");
                            }
                        }
                    }
                    catch (RetiredException)
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "BlockManager Block handler retired");

                        if (options.InternalProfiling)
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Block handler - Req: {sw_req!.ElapsedMilliseconds}ms, Resp: {sw_resp!.ElapsedMilliseconds}ms, Cache: {sw_cache!.ElapsedMilliseconds}ms, Get: {sw_get!.ElapsedMilliseconds}ms");
                        }

                        cache.Retire();
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "BlockHandlerError", ex, "Error in block handler");
                        req.Retire();
                        res.Retire();
                        cache.Retire();
                    }
                })).ToArray();

                await Task.WhenAll([volume_consumer, .. block_handlers]).ConfigureAwait(false);
            });
        }
    }

}
