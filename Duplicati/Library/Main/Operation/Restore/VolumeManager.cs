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

    /// <summary>
    /// Process that manages the volumes that the `VolumeDownloader` process has downloaded.
    /// It is responsible for fetching the volumes from the backend and caching them.
    /// </summary>
    internal class VolumeManager
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeManager>();

        /// <summary>
        /// Runs the volume manager process.
        /// </summary>
        /// <param name="db">The local restore database, used to find the volume where a block is stored.</param>
        /// <param name="backend">The backend manager, used to fetch the volumes from the backend.</param>
        /// <param name="options">The restore options.</param>
        /// <param name="results">The restore results.</param>
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
                                var ex = new InvalidOperationException("Unexpected message type");
                                Logging.Log.WriteErrorMessage(LOGTAG, "UnexpectedMessage", ex, "Unexpected message type");
                                throw ex;
                        }
                    }
                }
            );
        }
    }

}