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
using System.Collections.Generic;
using System.Threading.Tasks;
using CoCoL;
using System.Linq;
using Duplicati.Library.Main.Database;
using static Duplicati.Library.Main.BackendManager;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class VolumeDownloader
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDownloader>();

        public static Task Run(LocalRestoreDatabase db, BackendManager backend, Options options, RestoreResults results)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.downloadRequest.ForRead,
                Output = Channels.downloadedVolume.ForWrite
            },
            async self =>
            {
                try
                {
                    Dictionary<long, IDownloadWaitHandle> cache = [];
                    using var cmd = db.Connection.CreateCommand();
                    cmd.CommandText = "SELECT Name, Size, Hash FROM RemoteVolume WHERE ID = ?";
                    cmd.AddParameter();

                    while (true)
                    {
                        var block_request = await self.Input.ReadAsync();

                        if (!cache.TryGetValue(block_request.VolumeID, out IDownloadWaitHandle f))
                        {
                            try
                            {
                                cmd.SetParameterValue(0, block_request.VolumeID);
                                var (volume_name, volume_size, volume_hash) = cmd.ExecuteReaderEnumerable().Select(x => (x.GetString(0), x.GetInt64(1), x.GetString(2))).First();
                                f = backend.GetAsync(volume_name, volume_size, volume_hash);
                                cache.Add(block_request.VolumeID, f);
                                // TODO Auto evict and delete tmp files if their references have been reached.
                                // TODO Also check if another local file already have the block, and if so, fetch it and shortcut the process network by delivering it straight to the BlockManager.
                            }
                            catch (Exception)
                            {
                                lock (results)
                                {
                                    results.BrokenRemoteFiles.Add(block_request.VolumeID);
                                }
                                throw;
                            }
                        }

                        await self.Output.WriteAsync((block_request, f));
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume downloader retired");
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "DownloadError", ex, "Error during download");
                    throw;
                }
            });
        }
    }
}