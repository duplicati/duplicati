using System;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class FileProcessor
    {
        public static Task Run(LocalRestoreDatabase db, ChannelMarkerWrapper<(long,long)> block_request, ChannelMarkerWrapper<byte[]> block_response)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.filesToRestore.ForRead,
                BlockRequest = block_request.ForWrite,
                BlockResponse = block_response.ForRead
            },
            async self =>
            {
                // TODO preallocate the file size to avoid fragmentation / help the operating system / filesystem. Verify this in a benchmark - I think it relies on OS and filesystem.
                // using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None) { fs.SetLength(size); fs.Seek(0, SeekOrigin.Begin); }

                //while (true)
                {
                    var file = await self.Input.ReadAsync();
                    if (file == null)
                    {
                        //break;
                    }
                    Console.WriteLine($"Got file to restore: '{file.Path}', {file.Length} bytes, {file.Hash}");

                    var blocks = db.Connection.CreateCommand().ExecuteReaderEnumerable(@$"SELECT Block.ID, Block.Hash, Block.Size, Block.VolumeID FROM BlocksetEntry  INNER JOIN Block ON BlocksetEntry.BlockID = Block.ID WHERE BlocksetEntry.BlocksetID = ""{file.BlocksetID}""").Select(x => (x.GetInt64(0), x.GetString(1), x.GetInt64(2), x.GetInt64(3))).ToList();

                    foreach (var (id, hash, size, vid) in blocks)
                    {
                        Console.WriteLine($"Requesting block {id}, {hash}, {size}, {vid}");
                        await self.BlockRequest.WriteAsync((id, vid));
                        var data = await self.BlockResponse.ReadAsync();
                        Console.WriteLine($"Got block {data.Length} bytes");
                    }
                }
            });
        }
    }
}