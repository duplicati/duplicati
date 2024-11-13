using System;
using System.Threading.Tasks;
using CoCoL;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class VolumeDecrypter
    {
        public static Task Run()
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.downloadedVolume.ForRead,
                Output = Channels.decryptedVolume.ForWrite
            },
            async self =>
            {
                try
                {
                    while (true)
                    {
                        var (block_request, volume) = await self.Input.ReadAsync();
                        await self.Output.WriteAsync((block_request, volume.Wait()));
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