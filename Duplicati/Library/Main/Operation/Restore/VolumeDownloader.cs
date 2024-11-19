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

        public static Task Run(LocalRestoreDatabase db, BackendManager backend, Options options)
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
                            catch (Exception ex)
                            {
                                Logging.Log.WriteErrorMessage(LOGTAG, "DownloadError", ex, $"Failed to download volume: '{block_request.VolumeID}'");
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