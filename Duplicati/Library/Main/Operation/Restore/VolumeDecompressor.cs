using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class VolumeDecompressor
    {
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
                    while (true)
                    {
                        var (block_request, volume) = await self.Input.ReadAsync();

                        byte[] buffer = new byte[block_request.BlockSize];
                        new BlockVolumeReader(options.CompressionModule, volume, options).ReadBlock(block_request.BlockHash, buffer);

                        await self.Output.WriteAsync((block_request, buffer));
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