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
                        var (block_id, volume_id, volume) = await self.Input.ReadAsync();
                        // NOP operation for now - decryption is handled by the backend during download. Should be done here to increase concurrency.
                        await self.Output.WriteAsync((block_id, volume_id, volume));
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