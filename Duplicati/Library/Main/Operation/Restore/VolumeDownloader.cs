using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;

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
                    while (true)
                    {
                        var request = await self.Input.ReadAsync();

                        // Handle internal exceptions? Maybe the next ones can do that.
                        var volume = new AsyncDownloader(new List<Database.IRemoteVolume>([request]), backend).FirstOrDefault();

                        self.Output.Write(volume);
                    }
                }
                catch (Exception ex)
                {
                    // Check the type of exception and handle it accordingly?
                }
            });
        }
    }
}