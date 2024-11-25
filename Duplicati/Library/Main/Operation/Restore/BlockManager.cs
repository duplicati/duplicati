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
            private long cache_max;
            /// <summary>
            /// The eviction ratio for the internal cache when full.
            /// </summary>
            private float eviction_ratio;

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
                        INNER JOIN Block ON Block.ID == BlocksetEntry.BlockID
                        WHERE Block.VolumeID IS NOT -1
                    )
                    GROUP BY BlockID
                ");
                cmd.ExecuteNonQuery($@"CREATE INDEX ""blockcount_{db.m_temptabsetguid}_idx"" ON ""blockcount_{db.m_temptabsetguid}"" (BlockID)");

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
            private void CheckCounts(BlockRequest blockRequest)
            {
                lock (m_blockcountdecrcmd)
                {
                    // Update the block count and check whether the block is still needed.
                    m_blockcountdecrcmd.SetParameterValue(0, blockRequest.BlockID);
                    var count = m_blockcountdecrcmd.ExecuteScalarInt64();
                    if (count > 0)
                    {
                        return;
                    }
                    else if (count == 0)
                    {
                        // Evict the block from the cache and check if the volume is no longer needed.
                        _dictionary.Remove(blockRequest.BlockID);
                        m_volumecountdecrcmd.SetParameterValue(0, blockRequest.VolumeID);
                        var volcount = m_volumecountdecrcmd.ExecuteScalarInt64();
                        if (volcount == 0)
                        {
                            m_volume_request.WriteAsync(new BlockRequest(-1, -1, "", -1, blockRequest.VolumeID, true));
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
                var tcs = new TaskCompletionSource<byte[]>();
                var new_tcs = _waiters.GetOrAdd(block_request.BlockID, tcs);
                if (new_tcs == tcs)
                { // We are the first to request this block
                    m_volume_request.WriteAsync(block_request);
                }
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

                // Compact the cache if it is too large.
                if (_dictionary.Count > cache_max)
                {
                    _dictionary.Compact(eviction_ratio);
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
                var blockcount = cmd.ExecuteScalarInt64($@"SELECT SUM(BlockCount) FROM ""blockcount_{db.m_temptabsetguid}""");
                var volumecount = cmd.ExecuteScalarInt64($@"SELECT SUM(BlockCount) FROM ""volumecount_{db.m_temptabsetguid}""");

                if (blockcount != 0)
                {
                    var blocks = cmd.ExecuteReaderEnumerable($@"
                        SELECT BlockID
                        FROM ""blockcount_{db.m_temptabsetguid}""
                        WHERE BlockCount > 0")
                        .Select(x => x.GetInt64(0))
                        .ToArray();
                    var blockids = string.Join(", ", blocks);
                    Logging.Log.WriteWarningMessage(LOGTAG, "BlockCountError", null, $"Block count in SleepableDictionarys block table is not zero: {blockcount}{Environment.NewLine}Blocks: {blockids}");
                }

                if (volumecount != 0)
                {
                    var vols = cmd.ExecuteReaderEnumerable($@"
                        SELECT VolumeID
                        FROM ""volumecount_{db.m_temptabsetguid}""
                        WHERE BlockCount > 0")
                        .Select(x => x.GetInt64(0))
                        .ToArray();
                    var volids = string.Join(", ", vols);
                    Logging.Log.WriteWarningMessage(LOGTAG, "VolumeCountError", null, $"Volume count in SleepableDictionarys volume table is not zero: {volumecount}{Environment.NewLine}Volumes: {volids}");
                }

                cmd.ExecuteNonQuery($@"DROP TABLE IF EXISTS ""blockcount_{db.m_temptabsetguid}""");
                cmd.ExecuteNonQuery($@"DROP TABLE IF EXISTS ""volumecount_{db.m_temptabsetguid}""");
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
                Input = Channels.decompressedVolumes.ForRead,
                Output = Channels.downloadRequest.ForWrite
            },
            async self =>
            {
                // Create a cache for the blocks,
                using SleepableDictionary cache = new(db, self.Output, options, fp_requests.Length);

                // The volume consumer will read blocks from the input channel (data blocks from the volumes) and store them in the cache.
                var volume_consumer = Task.Run(async () => {
                    try
                    {
                        while (true)
                        {
                            var (block_request, data) = await self.Input.ReadAsync();
                            cache.Set(block_request, data);
                        }
                    }
                    catch (RetiredException)
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "BlockManager Volume consumer retired");

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
                    try
                    {
                        while (true)
                        {
                            var block_request = await req.ReadAsync();
                            var data = await cache.Get(block_request);
                            await res.WriteAsync(data);
                        }
                    }
                    catch (RetiredException)
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "BlockManager Block handler retired");

                        cache.Retire();
                    }
                })).ToArray();

                await Task.WhenAll([volume_consumer, ..block_handlers]);
            });
        }
    }

}
