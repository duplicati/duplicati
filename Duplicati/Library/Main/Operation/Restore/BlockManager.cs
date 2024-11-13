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
            private readonly IWriteChannel<BlockRequest> m_volume_request;
            private readonly MemoryCache _dictionary;
            private readonly ConcurrentDictionary<long, TaskCompletionSource<byte[]>> _waiters = new();

            public SleepableDictionary(IWriteChannel<BlockRequest> volume_request)
            {
                m_volume_request = volume_request;
                var cache_options = new MemoryCacheOptions();
                _dictionary = new MemoryCache(cache_options);
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
                _dictionary.Set(key, value);
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

            public void CancelAll()
            {
                foreach (var tcs in _waiters.Values)
                {
                    tcs.SetException(new RetiredException("Request waiter"));
                }
                m_volume_request.Retire();
            }
        }

        public static Task Run(IChannel<BlockRequest>[] fp_requests, IChannel<byte[]>[] fp_responses)
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
                SleepableDictionary cache = new(self.Output);

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
                        // NOP
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
                        cache.CancelAll();
                    }
                })).ToArray();

                await Task.WhenAll([volume_consumer, ..block_handlers]);
            });
        }
    }
}