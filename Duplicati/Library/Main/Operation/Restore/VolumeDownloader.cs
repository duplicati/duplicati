using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Utility;
using static Duplicati.Library.Main.BackendManager;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal static class VolumeDownloader
    {
        public static Task Run(BackendManager backend, Options options)
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

                    while (true)
                    {
                        var (block_id, volume_id, request) = await self.Input.ReadAsync();

                        if (!cache.TryGetValue(volume_id, out IDownloadWaitHandle f))
                        {
                            try
                            {
                                f = backend.GetAsync(request.Name, request.Size, request.Hash);
                                cache.Add(volume_id, f);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to download volume: '{request.Name}' | {ex.Message}");
                                throw;
                            }
                        }

                        self.Output.Write((block_id, volume_id, f));
                    }
                }
                catch (RetiredException ex)
                {
                    // NOP
                }
            });
        }
    }
}