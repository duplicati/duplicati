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
    internal class BlockManager
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BlockManager>();

        internal class SleepableDictionary : IDisposable
        {
            private readonly IWriteChannel<BlockRequest> m_volume_request;
            private readonly MemoryCache _dictionary;
            private readonly ConcurrentDictionary<long, TaskCompletionSource<byte[]>> _waiters = new();
            private readonly string m_temptabsetguid = Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            private readonly LocalRestoreDatabase db;
            private readonly IDbCommand m_blockcountcmd;
            private readonly IDbCommand m_blockcountdecrcmd;
            private int readers = 0;

            public SleepableDictionary(LocalRestoreDatabase db, IWriteChannel<BlockRequest> volume_request, int readers)
            {
                m_volume_request = volume_request;
                var cache_options = new MemoryCacheOptions();
                _dictionary = new MemoryCache(cache_options);
                this.readers = readers;
                this.db = db;
                var cmd = db.Connection.CreateCommand();
                cmd.ExecuteNonQuery($@"DROP TABLE IF EXISTS ""blockcount_{m_temptabsetguid}""");
                cmd.ExecuteNonQuery($@"CREATE TEMP TABLE ""blockcount_{m_temptabsetguid}"" (BlockID INTEGER PRIMARY KEY, Count INTEGER)");
                // TODO Ensure that it only counts the blocks that should be restored.
                cmd.ExecuteNonQuery($@"
                    INSERT INTO ""blockcount_{m_temptabsetguid}""
                    SELECT BlockID, COUNT(*)
                    FROM (
                        SELECT BlockID
                        FROM BlocksetEntry
                        INNER JOIN ""{db.m_tempfiletable}"" ON BlocksetEntry.BlocksetID = ""{db.m_tempfiletable}"".BlocksetID
                    )
                    GROUP BY BlockID
                ");
                cmd.ExecuteNonQuery($@"CREATE INDEX ""blockcount_{m_temptabsetguid}_idx"" ON ""blockcount_{m_temptabsetguid}"" (BlockID)");

                m_blockcountcmd = db.Connection.CreateCommand();
                m_blockcountcmd.CommandText = $@"SELECT Count FROM ""blockcount_{m_temptabsetguid}"" WHERE BlockID = ?";
                m_blockcountcmd.AddParameter();

                m_blockcountdecrcmd = db.Connection.CreateCommand();
                m_blockcountdecrcmd.CommandText = $@"UPDATE ""blockcount_{m_temptabsetguid}"" SET Count = Count - 1 WHERE BlockID = ?";
                m_blockcountdecrcmd.AddParameter();
            }

            public Task<byte[]> Get(BlockRequest block_request)
            {
                if (_dictionary.TryGetValue(block_request.BlockID, out byte[] value))
                {
                    return Task.FromResult(value);
                }

                var tcs = new TaskCompletionSource<byte[]>();
                tcs = _waiters.GetOrAdd(block_request.BlockID, tcs);
                m_volume_request.WriteAsync(block_request);
                return tcs.Task;
            }

            public void Set(long key, byte[] value)
            {
                m_blockcountcmd.SetParameterValue(0, key);
                var count = m_blockcountcmd.ExecuteScalarInt64();
                if (count > 1) {
                    _dictionary.Set(key, value);
                }
                else if (count == 1) {
                    _dictionary.Remove(key);
                }
                else if (count == 0) {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BlockCountError", null, $"Block {key} has a count of 0");
                }
                m_blockcountdecrcmd.SetParameterValue(0, key);
                m_blockcountdecrcmd.ExecuteNonQuery();

                if (_waiters.TryRemove(key, out var tcs))
                {
                    tcs.SetResult(value);
                }

                // TODO Make this a configurable value
                // TODO Current eviction policy evicts 50 %. Maybe make this a configurable value?
                if (_dictionary.Count > 8 * 1024)
                {
                    _dictionary.Compact(0.5);
                }
            }

            public void Retire()
            {
                if (Interlocked.Decrement(ref readers) <= 0) {
                    m_volume_request.Retire();
                }
            }

            public void Dispose()
            {
                db.Connection.CreateCommand().ExecuteNonQuery($@"DROP TABLE IF EXISTS ""blockcount_{m_temptabsetguid}""");
            }

            public void CancelAll()
            {
                foreach (var tcs in _waiters.Values)
                {
                    tcs.SetException(new RetiredException("Request waiter"));
                }
            }
        }

        public static Task Run(LocalRestoreDatabase db, IChannel<BlockRequest>[] fp_requests, IChannel<byte[]>[] fp_responses)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.decompressedVolumes.ForRead,
                Output = Channels.downloadRequest.ForWrite
            },
            async self =>
            {
                // TODO at some point, this should include some kind of cache eviction policy
                using SleepableDictionary cache = new(db, self.Output, fp_requests.Length);

                var volume_consumer = Task.Run(async () => {
                    try
                    {
                        while (true)
                        {
                            var (block_request, data) = await self.Input.ReadAsync();
                            cache.Set(block_request.BlockID, data);
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