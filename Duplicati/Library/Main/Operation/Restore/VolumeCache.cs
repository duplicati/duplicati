using CoCoL;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Restore
{

    internal class VolumeCache
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeCache>();

        public static Task Run(LocalRestoreDatabase db, BackendManager backend, Options options, RestoreResults results)
        {
            return AutomationExtensions.RunTask(
                new
                {
                    VolumeRequest = Channels.BlockFetch.ForRead,
                    DecompressRequest = Channels.DecompressionRequest.ForWrite,
                    DownloadRequest = Channels.DownloadRequest.ForWrite,
                    DownloadResponse = Channels.DecryptedVolume.ForRead,
                },
                async self =>
                {
                    Dictionary<long, BlockVolumeReader> cache = [];
                    Dictionary<long, TempFile> files = [];
                    Dictionary<long, List<BlockRequest>> in_flight = [];

                    // Prepare the command to get the volume information
                    using var cmd = db.Connection.CreateCommand();
                    cmd.CommandText = "SELECT Name, Size, Hash FROM RemoteVolume WHERE ID = ?";
                    cmd.AddParameter();

                    while (true)
                    {
                        var msg = (await MultiChannelAccess.ReadFromAnyAsync(self.VolumeRequest, self.DownloadResponse)).Value;

                        switch (msg)
                        {
                            case BlockRequest request:
                                {
                                    if (request.CacheDecrEvict)
                                    {
                                        cache.Remove(request.VolumeID, out var reader);
                                        reader?.Dispose();
                                        files.Remove(request.VolumeID, out var tempfile);
                                        tempfile?.Dispose();
                                    }
                                    else
                                    {
                                        if (cache.TryGetValue(request.VolumeID, out BlockVolumeReader reader))
                                        {
                                            await self.DecompressRequest.WriteAsync((request, reader));
                                        }
                                        else
                                        {
                                            if (in_flight.TryGetValue(request.VolumeID, out var waiters))
                                            {
                                                waiters.Add(request);
                                            }
                                            else
                                            {
                                                cmd.SetParameterValue(0, request.VolumeID);
                                                var (volume_name, volume_size, volume_hash) = cmd.ExecuteReaderEnumerable()
                                                    .Select(x => (x.GetString(0), x.GetInt64(1), x.GetString(2))).First();
                                                var handle = backend.GetAsync(volume_name, volume_size, volume_hash);
                                                await self.DownloadRequest.WriteAsync((request.VolumeID, handle));
                                                in_flight[request.VolumeID] = [request];
                                            }
                                        };
                                    }
                                    break;
                                }
                            case (long volume_id, TempFile temp_file, BlockVolumeReader reader):
                                {
                                    cache[volume_id] = reader;
                                    files[volume_id] = temp_file;
                                    foreach (var request in in_flight[volume_id])
                                    {
                                        await self.DecompressRequest.WriteAsync((request, reader));
                                    }
                                    in_flight.Remove(volume_id);
                                    break;
                                }
                            default:
                                throw new InvalidOperationException("Unexpected message type");
                        }
                    }
                }
            );
        }
    }

}