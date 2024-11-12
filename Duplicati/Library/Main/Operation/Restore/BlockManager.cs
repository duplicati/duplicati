using System.Collections.Concurrent;
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
        internal class SleepableDictionary // That also auto requests!
        {
            private readonly LocalRestoreDatabase m_db;
            private readonly IWriteChannel<(long,long,IRemoteVolume)> m_volume_request;
            private readonly MemoryCache _dictionary;
            private readonly ConcurrentDictionary<long, TaskCompletionSource<byte[]>> _waiters = new();
            private readonly ConcurrentDictionary<long, bool> _in_flight = new();

            public SleepableDictionary(LocalRestoreDatabase db, IWriteChannel<(long,long,IRemoteVolume)> volume_request)
            {
                m_db = db;
                m_volume_request = volume_request;
                var cache_options = new MemoryCacheOptions();
                _dictionary = new MemoryCache(cache_options);
            }

            public Task<byte[]> Get(long key, long volume_id)
            {
                if (_dictionary.TryGetValue(key, out byte[] value))
                {
                    return Task.FromResult(value);
                }

                // TODO Track in-flight, and local volumes.
                if (!_in_flight.ContainsKey(volume_id))
                {
                    var remote_volume = m_db.Connection.CreateCommand().ExecuteReaderEnumerable(@$"SELECT Name, Hash, Size FROM RemoteVolume WHERE ID = ""{volume_id}""").Select(x => new RemoteVolume(x.GetString(0), x.GetString(1), x.GetInt64(2))).First();
                    m_volume_request.WriteAsync((volume_id, remote_volume));
                    _in_flight[volume_id] = true;
                }

                var tcs = new TaskCompletionSource<byte[]>();
                _waiters.GetOrAdd(key, tcs);
                return tcs.Task;
            }

            public void Set(long key, byte[] value)
            {
                _dictionary.Set(key, value);
                if (_waiters.TryRemove(key, out var tcs))
                {
                    tcs.SetResult(value);
                }
                // TODO Make this a configurable value
                // TODO Current eviction policy evicts 50 %. Maybe make this a configurable value?
                if (_dictionary.Count > 32 * 1024)
                {
                    _dictionary.Compact(0.5);
                }
            }

            public void CancelAll()
            {
                foreach (var tcs in _waiters.Values)
                {
                    tcs.SetException(new RetiredException("Request waiter"));
                }
                m_volume_request.Retire();
            }
        }

        public static Task Run(LocalRestoreDatabase db, IChannel<(long,long)>[] fp_requests, IChannel<byte[]>[] fp_responses)
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
                SleepableDictionary cache = new(db, self.Output);

                var volume_consumer = Task.Run(async () => {
                    try
                    {
                        while (true)
                        {
                            var (block_id, data) = await self.Input.ReadAsync();
                            cache.Set(block_id, data);
                        }
                    }
                    catch (RetiredException)
                    {
                        // NOP
                    }
                });
                var block_handlers = fp_requests.Zip(fp_responses, (req, res) => Task.Run(async () => {
                    try
                    {
                        while (true)
                        {
                            var (blockid, vid) = await req.ReadAsync();
                            var data = await cache.Get(blockid, vid);
                            await res.WriteAsync(data);
                        }
                    }
                    catch (RetiredException)
                    {
                        cache.CancelAll();
                    }
                })).ToArray();

                await Task.WhenAll([volume_consumer, ..block_handlers]);
            });
        }
    }
}