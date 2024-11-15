using System;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class VolumeDecompressor
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<VolumeDecompressor>();

        public static Task Run(Options options)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.decryptedVolume.ForRead,
                Output = Channels.decompressedVolumes.ForWrite
            },
            async self =>
            {
                try {
                    using var block_hasher = HashFactory.CreateHasher(options.BlockHashAlgorithm);

                    while (true)
                    {
                        var (block_request, volume) = await self.Input.ReadAsync();

                        byte[] buffer = new byte[block_request.BlockSize];
                        new BlockVolumeReader(options.CompressionModule, volume, options).ReadBlock(block_request.BlockHash, buffer);
                        var hash = Convert.ToBase64String(block_hasher.ComputeHash(buffer, 0, (int)block_request.BlockSize));

                        if (hash != block_request.BlockHash)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "InvalidBlock", null, $"Invalid block detected for block {block_request.BlockID} in volume {block_request.VolumeID}, expected hash: {block_request.BlockHash}, actual hash: {hash}");
                        }

                        await self.Output.WriteAsync((block_request, buffer));
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "Volume decompressor retired");
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "DecompressionError", ex, "Error during decompression");
                    throw;
                }
            });
        }
    }
}