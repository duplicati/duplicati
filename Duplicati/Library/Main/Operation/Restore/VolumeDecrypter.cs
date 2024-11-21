using System;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class VolumeDecrypter
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDecrypter>();

        public static Task Run(RestoreResults results)
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
                        TempFile f = null;
                        try
                        {
                            f = volume.Wait();
                        }
                        catch (Exception)
                        {
                            lock (results)
                            {
                                results.BrokenRemoteFiles.Add(block_request.VolumeID);
                            }
                            throw;
                        }

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