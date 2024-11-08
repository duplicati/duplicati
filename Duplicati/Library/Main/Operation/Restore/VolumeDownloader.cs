using System;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Utility;

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
                while (true)
                {
                    var request = await self.Input.ReadAsync();

                    Console.WriteLine($"Got volume to download: '{request.Name}', {request.Size} bytes, {request.Hash}");

                    TempFile f = null;
                    try
                    {
                        f = backend.GetAsync(request.Name, request.Size, request.Hash).Wait();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to download volume: '{request.Name}' | {ex.Message}");
                        continue;
                    }

                    Console.WriteLine($"Downloaded volume: '{f.Name}'");

                    self.Output.Write(f);
                }
            });
        }
    }
}