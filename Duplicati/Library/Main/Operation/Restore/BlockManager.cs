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
        internal class SleepableDictionary(LocalRestoreDatabase db, IWriteChannel<(long,IRemoteVolume)> volume_request) // That also auto requests!
        {
            private readonly LocalRestoreDatabase m_db = db;
            private readonly IWriteChannel<(long,IRemoteVolume)> m_volume_request = volume_request;
            private readonly ConcurrentDictionary<long, byte[]> _dictionary = new();
            private readonly ConcurrentDictionary<long, TaskCompletionSource<byte[]>> _waiters = new();

            public Task<byte[]> Get(long key, long volume_id)
            {
                if (_dictionary.TryGetValue(key, out var value))
                {
                    return Task.FromResult(value);
                }

                // TODO Track in-flight, and local volumes.
                var remote_volume = m_db.Connection.CreateCommand().ExecuteReaderEnumerable(@$"SELECT Name, Hash, Size FROM RemoteVolume WHERE ID = ""{volume_id}""").Select(x => new RemoteVolume(x.GetString(0), x.GetString(1), x.GetInt64(2))).First();
                m_volume_request.WriteAsync((volume_id, remote_volume));

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
                    while (true)
                    {
                        var (block_id, data) = await self.Input.ReadAsync();
                        cache.Set(block_id, data);
                    }
                });
                var block_handlers = fp_requests.Zip(fp_responses, (req, res) => Task.Run(async () => {
                    while (true)
                    {
                        var (blockid, vid) = await req.ReadAsync();
                        var data = await cache.Get(blockid, vid);
                        await res.WriteAsync(data);
                    }
                })).ToArray();

                await Task.WhenAll([volume_consumer, ..block_handlers]);
            });
        }
    }
}