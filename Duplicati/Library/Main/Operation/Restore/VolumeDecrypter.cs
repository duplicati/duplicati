using System;
using System.Threading.Tasks;
using CoCoL;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class VolumeDecrypter
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDecrypter>();

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
                        var f = volume.Wait();
                        await self.Output.WriteAsync((block_request, f));
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume decrypter retired");
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "DecryptionError", ex, "Error during decryption");
                    throw;
                }
            });
        }
    }
}