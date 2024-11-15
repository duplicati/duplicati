using System;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class FileProcessor
    {
        public static Task Run(LocalRestoreDatabase db, IChannel<BlockRequest> block_request, IChannel<byte[]> block_response, RestoreResults results)
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

                        var blocks = db.Connection
                            .CreateCommand()
                            .ExecuteReaderEnumerable(@$"
                                SELECT Block.ID, Block.Hash, Block.Size, Block.VolumeID
                                FROM BlocksetEntry INNER JOIN Block
                                ON BlocksetEntry.BlockID = Block.ID
                                WHERE BlocksetEntry.BlocksetID = ""{file.BlocksetID}"""
                            )
                            .Select(x =>
                                new BlockRequest(x.GetInt64(0), x.GetString(1), x.GetInt64(2), x.GetInt64(3))
                            )
                            .ToList();

                        long bytes_written = 0;

                        if (blocks.Count == 1 && blocks[0].BlockSize == 0)
                        {
                            // Create an empty file
                            using var fs = new System.IO.FileStream(file.Path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.None);
                        }
                        else
                        {
                            if (blocks.Any(x => x.VolumeID < 0))
                            {
                                Console.WriteLine($"{file.Path} has a negative volume ID and positive size, skipping");
                                continue;
                            }

                            using var fs = new System.IO.FileStream(file.Path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.None);

                            // TODO burst should be an option and should relate to the channel depth
                            int burst = 8;
                            for (int i = 0; i < blocks.Count; i += burst)
                            {
                                int this_burst = Math.Min(burst, blocks.Count - i);
                                for (int j = 0; j < this_burst; j++)
                            {
                                    await block_request.WriteAsync(blocks[i + j]);
                                }
                                for (int j = 0; j < this_burst; j++)
                                {
                                var data = await block_response.ReadAsync();
                                await fs.WriteAsync(data);
                                bytes_written += data.Length;

                                }
                            }
                        }

                        lock (results)
                        {
                            results.RestoredFiles++;
                            results.SizeOfRestoredFiles += bytes_written;
                        }
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