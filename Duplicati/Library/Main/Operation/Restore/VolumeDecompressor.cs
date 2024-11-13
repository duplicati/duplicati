using System;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class VolumeDecompressor
    {
        public static Task Run(LocalRestoreDatabase db, Options options)
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
                        var (block_id, volume_id, volume) = await self.Input.ReadAsync();

                        var reader = db.Connection.CreateCommand().ExecuteReader(@$"SELECT Hash, Size FROM Block WHERE ID = {block_id}");
                        reader.Read();
                        var bhash = reader.GetString(0);
                        var bsize = reader.GetInt64(1);
                        reader.Close();
                        byte[] buffer = new byte[options.Blocksize];
                        new BlockVolumeReader(options.CompressionModule, volume, options).ReadBlock(bhash, buffer);

                        await self.Output.WriteAsync((block_id, buffer[..(int)bsize]));
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