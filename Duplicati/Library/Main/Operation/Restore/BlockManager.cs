using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class BlockManager
    {
        internal class SleepableDictionary
        {
            private readonly ConcurrentDictionary<long, byte[]> _dictionary = new();
            private readonly ConcurrentDictionary<long, TaskCompletionSource<byte[]>> _waiters = new();

            public Task<byte[]> Get(long key)
            {
                if (_dictionary.TryGetValue(key, out var value))
                {
                    return Task.FromResult(value);
                }

                var tcs = new TaskCompletionSource<byte[]>();
                _waiters.GetOrAdd(key, tcs);
                return tcs.Task;
            }

            public void Set(long key, byte[] value)
            {
                _dictionary.TryAdd(key, value);
                if (_waiters.TryRemove(key, out var tcs))
                {
                    tcs.SetResult(value);
                }
            }
        }

        public static Task Run(LocalRestoreDatabase db, ChannelMarkerWrapper<long>[] fp_requests, ChannelMarkerWrapper<byte[]>[] fp_responses)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.decompressedVolumes.ForRead,
                Output = Channels.volumeRequest.ForWrite,
                Requests = fp_requests.Select(x => x.ForRead).ToArray(),
                Responses = fp_responses.Select(x => x.ForWrite).ToArray()
            },
            async self =>
            {
                // TODO at some point, this should include some kind of cache eviction policy
                SleepableDictionary cache = new();

                var volume_consumer = Task.Run(async () => {
                    while (true)
                    {
                        var volume = await self.Input.ReadAsync();
                        foreach ((var blockid, var data) in volume)
                        {
                            cache.Set(blockid, data);
                        }
                    }
                });
                var block_handlers = self.Requests.Zip(self.Responses, (req, res) => Task.Run(async () => {
                    while (true)
                    {
                        var blockid = await req.ReadAsync();
                        var data = await cache.Get(blockid);
                        await res.WriteAsync(data);
                    }
                })).ToArray();

                await Task.WhenAll([volume_consumer, ..block_handlers]);
            });
        }
    }
}