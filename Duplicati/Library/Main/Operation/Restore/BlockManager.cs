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
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
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
            private readonly IWriteChannel<BlockRequest> m_volume_request;
            /// <summary>
            /// The dictionary holding the cached blocks.
            /// </summary>
            private readonly MemoryCache _dictionary;
            /// <summary>
            /// The dictionary holding the `Tasks` for each block request in flight.
            /// </summary>
            private readonly ConcurrentDictionary<long, TaskCompletionSource<byte[]>> _waiters = new();
            /// <summary>
            /// The database holding (amongst other information) information about how many of each block this restore requires.
            /// </summary>
            private readonly LocalRestoreDatabase db;
            /// <summary>
            /// The command for decrementing the block count for a block.
            /// </summary>
            private readonly IDbCommand m_blockcountdecrcmd;
            /// <summary>
            /// The command for decrementing the block count for a volume.
            /// </summary>
            private readonly IDbCommand m_volumecountdecrcmd;
            /// <summary>
            /// The number of readers accessing this dictionary. Used during shutdown / cleanup.
            /// </summary>
            private int readers = 0;
            /// <summary>
            /// The maximum size of the internal cache in number of blocks.
            /// </summary>
            private readonly long cache_max;
            /// <summary>
            /// The eviction ratio for the internal cache when full.
            /// </summary>
            private readonly float eviction_ratio;

            Stopwatch sw_checkcounts = new ();
            Stopwatch sw_get_decompress = new ();
            Stopwatch sw_get_wait = new ();
            Stopwatch sw_get_verify = new ();
            private readonly ConcurrentDictionary<long, long> m_blockcount = new ();
            private readonly ConcurrentDictionary<long, long> m_volumecount = new ();

            /// <summary>
            /// Initializes a new instance of the <see cref="SleepableDictionary"/> class.
            /// </summary>
            /// <param name="db">The database holding information about how many of each block this restore requires.</param>
            /// <param name="volume_request">Channel for submitting block requests from a volume.</param>
            /// <param name="readers">Number of readers accessing this dictionary. Used during shutdown / cleanup.</param>
            public SleepableDictionary(LocalRestoreDatabase db, IWriteChannel<BlockRequest> volume_request, Options options, int readers)
            {
                m_volume_request = volume_request;
                var cache_options = new MemoryCacheOptions();
                _dictionary = new MemoryCache(cache_options);
                this.readers = readers;
                this.db = db;

                var cmd = db.Connection.CreateCommand();
                cmd.ExecuteNonQuery($@"DROP TABLE IF EXISTS ""blockcount_{db.m_temptabsetguid}""");
                cmd.ExecuteNonQuery($@"CREATE TEMP TABLE ""blockcount_{db.m_temptabsetguid}"" (BlockID INTEGER PRIMARY KEY, BlockCount INTEGER)");
                cmd.ExecuteNonQuery($@"
                    INSERT INTO ""blockcount_{db.m_temptabsetguid}""
                    SELECT BlockID, COUNT(*)
                    FROM (
                        SELECT BlockID
                        FROM BlocksetEntry
                        INNER JOIN ""{db.m_tempfiletable}"" ON BlocksetEntry.BlocksetID = ""{db.m_tempfiletable}"".BlocksetID
                    )
                    GROUP BY BlockID
                    "
                    + (options.SkipMetadata ? "" : $@"
                    UNION ALL
                    SELECT BlockID, COUNT(*)
                    FROM (
                        SELECT BlockID
                        FROM ""{db.m_tempfiletable}""
                        INNER JOIN Metadataset ON ""{db.m_tempfiletable}"".MetadataID = Metadataset.ID
                        INNER JOIN BlocksetEntry ON Metadataset.BlocksetID = BlocksetEntry.BlocksetID
                        WHERE ""{db.m_tempfiletable}"".BlocksetID IS NOT {LocalDatabase.FOLDER_BLOCKSET_ID}
                    )
                    GROUP BY BlockID
                "));
                cmd.ExecuteNonQuery($@"CREATE INDEX ""blockcount_{db.m_temptabsetguid}_idx"" ON ""blockcount_{db.m_temptabsetguid}"" (BlockID)");

                foreach (var row in cmd.ExecuteReaderEnumerable($@"
                    SELECT BlockID, BlockCount
                    FROM ""blockcount_{db.m_temptabsetguid}""
                    WHERE BlockCount > 1"))
                {
                    m_blockcount.TryAdd(row.GetInt64(0), row.GetInt64(1));
                }
                Console.WriteLine($"Block count: {m_blockcount.Count}");

                cmd.ExecuteNonQuery($@"DROP TABLE IF EXISTS ""volumecount_{db.m_temptabsetguid}""");
                cmd.ExecuteNonQuery($@"CREATE TEMP TABLE ""volumecount_{db.m_temptabsetguid}"" (VolumeID INTEGER PRIMARY KEY, BlockCount INTEGER)");
                cmd.ExecuteNonQuery($@"
                    INSERT INTO ""volumecount_{db.m_temptabsetguid}""
                    SELECT VolumeID, COUNT(*)
                    FROM (
                        SELECT VolumeID, BlockCount
                        FROM Block
                        INNER JOIN ""blockcount_{db.m_temptabsetguid}"" ON Block.ID = ""blockcount_{db.m_temptabsetguid}"".BlockID
                    )
                    GROUP BY VolumeID
                ");
                cmd.ExecuteNonQuery($@"CREATE INDEX ""volumecount_{db.m_temptabsetguid}_idx"" ON ""volumecount_{db.m_temptabsetguid}"" (VolumeID)");

                foreach (var row in cmd.ExecuteReaderEnumerable($@"
                    SELECT VolumeID, BlockCount
                    FROM ""volumecount_{db.m_temptabsetguid}""
                    WHERE BlockCount > 0"))
                {
                    m_volumecount.TryAdd(row.GetInt64(0), row.GetInt64(1));
                }
                Console.WriteLine($"Volume count: {m_volumecount.Count}");

                m_blockcountdecrcmd = db.Connection.CreateCommand();
                m_blockcountdecrcmd.CommandText = $@"UPDATE ""blockcount_{db.m_temptabsetguid}"" SET BlockCount = BlockCount - 1 WHERE BlockID = ? RETURNING BlockCount";
                m_blockcountdecrcmd.AddParameter();

                m_volumecountdecrcmd = db.Connection.CreateCommand();
                m_volumecountdecrcmd.CommandText = $@"UPDATE ""volumecount_{db.m_temptabsetguid}"" SET BlockCount = BlockCount - 1 WHERE VolumeID = ? RETURNING BlockCount";
                m_volumecountdecrcmd.AddParameter();

                // Assumes that the RestoreCacheMax is divisable by the blocksize
                cache_max = options.RestoreCacheMax / options.Blocksize;
                eviction_ratio = options.RestoreCacheEvict;
            }

            /// <summary>
            /// Decrements the block counters for the block and volume, and checks
            /// if the block is still needed. If the block is no longer needed, it
            /// will be removed from the cache. If the volume is no longer needed,
            /// the VolumeDownloader will be notified.
            /// </summary>
            /// <param name="blockRequest">The block request to check.</param>
            /// <param name="decrement"></param>
            public void CheckCounts(BlockRequest blockRequest)
            {
                lock (m_blockcountdecrcmd)
                {
                    sw_checkcounts.Start();
                    // Decrement the block count.
                    var count = m_blockcount.TryGetValue(blockRequest.BlockID, out var c) ? c-1 : 0;
                    if (count > 0)
                    {
                        m_blockcount[blockRequest.BlockID] = count;
                        sw_checkcounts.Stop();
                        return;
                    }
                    else if (count == 0)
                    {
                        // Evict the block from the cache and check if the volume is no longer needed.
                        m_blockcount.TryRemove(blockRequest.BlockID, out _);
                        _dictionary.Remove(blockRequest.BlockID);
                        var volcount = m_volumecount.TryGetValue(blockRequest.VolumeID, out var vc) ? vc-1 : 0;
                        if (volcount > 0)
                        {
                            m_volumecount[blockRequest.VolumeID] = volcount;
                        }
                        else if (volcount == 0)
                        {
                            m_volumecount.TryRemove(blockRequest.VolumeID, out _);
                            blockRequest.CacheDecrEvict = true;
                            m_volume_request.Write(blockRequest);
                        }
                        if (volcount < 0)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "VolumeCountError", null, $"Volume {blockRequest.VolumeID} has a count below 0");
                        }
                    }
                    else if (count < 0)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "BlockCountError", null, $"Block {blockRequest.BlockID} has a count below 0");
                    }
                    sw_checkcounts.Stop();
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
                // Check if the block is already in the cache, and return it if it is.
                if (_dictionary.TryGetValue(block_request.BlockID, out byte[] value))
                {
                    CheckCounts(block_request);
                    return Task.FromResult(value);
                }

                // If the block is not in the cache, request it from the volume.
                sw_get_wait.Start();
                var tcs = new TaskCompletionSource<byte[]>();
                var new_tcs = _waiters.GetOrAdd(block_request.BlockID, tcs);
                if (tcs == new_tcs)
                { // We are the first to request this block
                    m_volume_request.Write(block_request);
                }
                sw_get_wait.Stop();
                return new_tcs.Task.ContinueWith(t => { CheckCounts(block_request); return t.Result; });
            }

            /// <summary>
            /// Set a block in the cache. If the block is already in the cache,
            /// it will be replaced. If the block is not in the cache, it will
            /// be added. If the block is no longer needed, it will be removed
            /// from the cache. If the block is requested while it is being
            /// removed, the requester will be given a `Task` that will be
            /// completed when the block is available.
            /// </summary>
            /// <param name="key"></param>
            /// <param name="value"></param>
            public void Set(BlockRequest blockRequest, byte[] value)
            {
                // Notify any waiters that the block is available.
                if (_waiters.TryRemove(blockRequest.BlockID, out var tcs))
                {
                    tcs.SetResult(value);
                }
            }

            /// <summary>
            /// Retire the dictionary. This will decrement the number of readers
            /// accessing the dictionary, and if there are no more readers, it
            /// will retire the volume request channel, effectively shutting
            /// down the restore process network.
            /// </summary>
            public void Retire()
            {
                if (Interlocked.Decrement(ref readers) <= 0) {
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
                var cmd = db.Connection.CreateCommand();
                var blockcount = m_blockcount.Sum(x => x.Value);
                var volumecount = m_volumecount.Sum(x => x.Value);

                if (blockcount != 0)
                {
                    var blocks = m_blockcount.Where(x => x.Value != 0).Select(x => x.Key).ToArray();
                    var blockids = string.Join(", ", blocks);
                    Logging.Log.WriteWarningMessage(LOGTAG, "BlockCountError", null, $"Block count in SleepableDictionarys block table is not zero: {blockcount}{Environment.NewLine}");//Blocks: {blockids}");
                }

                if (volumecount != 0)
                {
                    var vols = m_volumecount.Where(x => x.Value != 0).Select(x => x.Key).ToArray();
                    var volids = string.Join(", ", vols);
                    Logging.Log.WriteWarningMessage(LOGTAG, "VolumeCountError", null, $"Volume count in SleepableDictionarys volume table is not zero: {volumecount}{Environment.NewLine}Volumes: {volids}");
                }

                cmd.ExecuteNonQuery($@"DROP TABLE IF EXISTS ""blockcount_{db.m_temptabsetguid}""");
                cmd.ExecuteNonQuery($@"DROP TABLE IF EXISTS ""volumecount_{db.m_temptabsetguid}""");

                Console.WriteLine($"Sleepable dictionary - CheckCounts: {sw_checkcounts.ElapsedMilliseconds}ms, Get wait: {sw_get_wait.ElapsedMilliseconds}ms, Get decompress: {sw_get_decompress.ElapsedMilliseconds}ms, Get verify: {sw_get_verify.ElapsedMilliseconds}ms");
            }

            /// <summary>
            /// Cancel all pending requests. This will set an exception on all
            /// pending requests, effectively cancelling them.
            /// </summary>
            public void CancelAll()
            {
                foreach (var tcs in _waiters.Values)
                {
                    tcs.SetException(new RetiredException("Request waiter"));
                }
            }
        }

        public static Task Run(LocalRestoreDatabase db, Options options, IChannel<BlockRequest>[] fp_requests, IChannel<byte[]>[] fp_responses)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.DecompressedBlock.ForRead,
                Output = Channels.BlockFetch.ForWrite
            },
            async self =>
            {
                // Create a cache for the blocks,
                using SleepableDictionary cache = new(db, self.Output, options, fp_requests.Length);

                // The volume consumer will read blocks from the input channel (data blocks from the volumes) and store them in the cache.
                var volume_consumer = Task.Run(async () => {
                    Stopwatch sw_read = options.InternalProfiling ? new () : null;
                    Stopwatch sw_set  = options.InternalProfiling ? new () : null;
                    try
                    {
                        while (true)
                        {
                            sw_read?.Start();
                            var (block_request, data) = await self.Input.ReadAsync();
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
                            Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", null, $"Read: {sw_read.ElapsedMilliseconds}ms, Set: {sw_set.ElapsedMilliseconds}ms");
                            Console.WriteLine($"Volume consumer - Read: {sw_read.ElapsedMilliseconds}ms, Set: {sw_set.ElapsedMilliseconds}ms");
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
                var block_handlers = fp_requests.Zip(fp_responses, (req, res) => Task.Run(async () => {
                    Stopwatch sw_req   = options.InternalProfiling ? new () : null;
                    Stopwatch sw_resp  = options.InternalProfiling ? new () : null;
                    Stopwatch sw_cache = options.InternalProfiling ? new () : null;
                    Stopwatch sw_get   = options.InternalProfiling ? new () : null;
                    try
                    {
                        while (true)
                        {
                            sw_req?.Start();
                            var block_request = await req.ReadAsync();
                            sw_req?.Stop();
                            if (block_request.CacheDecrEvict)
                            {
                                sw_cache?.Start();
                                // Target file already had the block.
                                cache.CheckCounts(block_request);
                                sw_cache?.Stop();
                            }
                            else
                            {
                                sw_get?.Start();
                                var data = await cache.Get(block_request);
                                sw_get?.Stop();
                                sw_resp?.Start();
                                await res.WriteAsync(data);
                                sw_resp?.Stop();
                            }
                        }
                    }
                    catch (RetiredException)
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "BlockManager Block handler retired");

                        if (options.InternalProfiling)
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", null, $"Req: {sw_req.ElapsedMilliseconds}ms, Resp: {sw_resp.ElapsedMilliseconds}ms, Cache: {sw_cache.ElapsedMilliseconds}ms, Get: {sw_get.ElapsedMilliseconds}ms");
                            Console.WriteLine($"Block handler - Req: {sw_req.ElapsedMilliseconds}ms, Resp: {sw_resp.ElapsedMilliseconds}ms, Cache: {sw_cache.ElapsedMilliseconds}ms, Get: {sw_get.ElapsedMilliseconds}ms");
                        }

                        cache.Retire();
                    }
                })).ToArray();

                await Task.WhenAll([volume_consumer, ..block_handlers]);
            });
        }
    }

}
