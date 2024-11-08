using System;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class FileProcessor
    {
        public static Task Run(LocalRestoreDatabase db, IChannel<(long,long)> block_request, IChannel<byte[]> block_response)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.filesToRestore.ForRead,
            },
            async self =>
            {
                // TODO preallocate the file size to avoid fragmentation / help the operating system / filesystem. Verify this in a benchmark - I think it relies on OS and filesystem.
                // using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None) { fs.SetLength(size); fs.Seek(0, SeekOrigin.Begin); }

                try
                {
                    while (true)
                    {
                        var file = await self.Input.ReadAsync();
                        if (file == null)
                        {
                            //break;
                        }
                        Console.WriteLine($"Got file to restore: '{file.Path}', {file.Length} bytes, {file.Hash}");

                        var blocks = db.Connection.CreateCommand().ExecuteReaderEnumerable(@$"SELECT Block.ID, Block.Hash, Block.Size, Block.VolumeID FROM BlocksetEntry INNER JOIN Block ON BlocksetEntry.BlockID = Block.ID WHERE BlocksetEntry.BlocksetID = ""{file.BlocksetID}""").Select(x => (x.GetInt64(0), x.GetString(1), x.GetInt64(2), x.GetInt64(3))).ToList();

                        if (blocks.Count == 1 && blocks[0].Item3 == 0)
                        {
                            // Create an empty file
                            using var fs = new System.IO.FileStream(file.Path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.None);
                        }
                        else
                        {
                            if (blocks.Any(x => x.Item4 < 0))
                            {
                                Console.WriteLine($"{file.Path} has a negative volume ID and positive size, skipping");
                                continue;
                            }

                            using var fs = new System.IO.FileStream(file.Path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.None);
                            foreach (var (id, hash, size, vid) in blocks)
                            {
                                await block_request.WriteAsync((id, vid));
                                var data = await block_response.ReadAsync();
                                Console.WriteLine($"Got block: {data.Length} bytes");
                                await fs.WriteAsync(data);
                            }
                        }

                        Console.WriteLine($"Restored file: '{file.Path}'");
                    }
                }
                catch (RetiredException ex)
                {
                    block_request.Retire();
                    block_response.Retire();
                    return;
                }
            });
        }
    }
}