using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class FileProcessor
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<FileProcessor>();

        public static Task Run(LocalRestoreDatabase db, IChannel<BlockRequest> block_request, IChannel<byte[]> block_response, RestoreResults results, Options options)
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
                    using var filehasher = HashFactory.CreateHasher(options.FileHashAlgorithm);
                    using var blockhasher = HashFactory.CreateHasher(options.BlockHashAlgorithm);
                    using var cmd = db.Connection.CreateCommand();
                    cmd.CommandText = @$"
                        SELECT Block.ID, Block.Hash, Block.Size, Block.VolumeID
                        FROM BlocksetEntry INNER JOIN Block
                        ON BlocksetEntry.BlockID = Block.ID
                        WHERE BlocksetEntry.BlocksetID = ?";
                    cmd.AddParameter();

                    while (true)
                    {
                        var file = await self.Input.ReadAsync();

                        cmd.SetParameterValue(0, file.BlocksetID);
                        var blocks = cmd.ExecuteReaderEnumerable()
                            .Select((b, i) =>
                                new BlockRequest(b.GetInt64(0), i, b.GetString(1), b.GetInt64(2), b.GetInt64(3))
                            )
                            .ToArray();

                        // TODO consistent argument ordering.
                        var missing_blocks = await VerifyTargetBlocks(file, blocks, filehasher, blockhasher, options, results);

                        if (missing_blocks.Count == 0)
                            continue;

                        long bytes_written = 0;

                        if (options.UseLocalBlocks && missing_blocks.Count > 0)
                        {
                            var (bw, new_missing_blocks) = await VerifyLocalBlocks(file, missing_blocks, blocks.Length, filehasher, blockhasher, options, results);
                            bytes_written = bw;
                            missing_blocks = new_missing_blocks;
                        }

                        if (blocks.Length == 1 && blocks[0].BlockSize == 0)
                        {
                            if (options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have created empty file ""{file.Path}""");
                            }
                            else
                            {
                                // Create an empty file, or truncate to 0
                                using var fs = new System.IO.FileStream(file.Path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.None);
                                fs.SetLength(0);
                            }
                        }
                        else if (missing_blocks.Count > 0)
                        {
                            if (blocks.Any(x => x.VolumeID < 0))
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "NegativeVolumeID", null, $"{file.Path} has a negative volume ID, skipping");
                                continue;
                            }

                            filehasher.Initialize();

                            try
                            {
                                using var fs = options.Dryrun ? new System.IO.FileStream(file.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read) : new System.IO.FileStream(file.Path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);

                                // TODO burst should be an option and should relate to the channel depth
                                int burst = 8;
                                int j = 0;
                                for (int i = 0; i < (int) Math.Min(missing_blocks.Count, burst); i++)
                                {
                                    await block_request.WriteAsync(missing_blocks[i]);
                                }
                                for (int i = 0; i < blocks.Length; i++)
                                {
                                    if (j < missing_blocks.Count && missing_blocks[j].BlockOffset == i)
                                    {
                                        var data = await block_response.ReadAsync();
                                        if (j < missing_blocks.Count - burst)
                                        {
                                            await block_request.WriteAsync(missing_blocks[j + burst]);
                                        }
                                        filehasher.TransformBlock(data, 0, data.Length, data, 0);
                                        if (options.Dryrun)
                                        {
                                            fs.Seek(fs.Position + blocks[i].BlockSize, System.IO.SeekOrigin.Begin);
                                        }
                                        else
                                        {
                                            await fs.WriteAsync(data);
                                        }
                                        bytes_written += data.Length;
                                        j++;
                                    }
                                    else
                                    {
                                        // Read the block to verify the file hash
                                        var data = new byte[blocks[i].BlockSize];
                                        var read = await fs.ReadAsync(data, 0, data.Length);
                                        // TODO the earlier step should have checked this block.
                                        //var bhash = blockhasher.ComputeHash(data, 0, data.Length);
                                        //if (Convert.ToBase64String(bhash) != blocks[i].BlockHash)
                                        //{
                                        //    Logging.Log.WriteWarningMessage(LOGTAG, "InvalidBlock", null, $"Invalid block detected for block index {i} {blocks[i].BlockID} in volume {blocks[i].VolumeID}, expected hash: {blocks[i].BlockHash}, actual hash: {Convert.ToBase64String(bhash)}");
                                        //}
                                        filehasher.TransformBlock(data, 0, read, data, 0);
                                    }
                                }

                                if (options.Dryrun)
                                {
                                    Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have restored {bytes_written} bytes of ""{file.Path}""");
                                }

                                filehasher.TransformFinalBlock([], 0, 0);
                                if (Convert.ToBase64String(filehasher.Hash) != file.Hash)
                                {
                                    Logging.Log.WriteErrorMessage(LOGTAG, "FileHashMismatch", null, $"File hash mismatch for {file.Path} - expected: {file.Hash}, actual: {Convert.ToBase64String(filehasher.Hash)}");
                                    throw new Exception("File hash mismatch");
                                }

                                if (fs?.Length > file.Length)
                                {
                                    if (options.Dryrun)
                                    {
                                        Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have truncated ""{file.Path}"" from {fs.Length} to {file.Length}");
                                    }
                                    else
                                    {
                                        fs.SetLength(file.Length);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                lock (results)
                                {
                                    results.BrokenLocalFiles.Add(file.Path);
                                }
                                throw;
                            }
                        }

                        lock (results)
                        {
                            results.RestoredFiles++;
                            results.SizeOfRestoredFiles += bytes_written;
                        }
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "File processor retired");
                    block_request.Retire();
                    block_response.Retire();
                    return;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "FileProcessingError", ex, "Error during file processing");
                    block_request.Retire();
                    block_response.Retire();
                    throw;
                }
            });
        }

        private static async Task<List<BlockRequest>> VerifyTargetBlocks(LocalRestoreDatabase.IFileToRestore file, BlockRequest[] blocks, System.Security.Cryptography.HashAlgorithm filehasher, System.Security.Cryptography.HashAlgorithm blockhasher, Options options, RestoreResults results)
        {
            // Check if the file exists
            List<BlockRequest> missing_blocks = [];
            // TODO It should check both the target path and the original path, as I think the original solution might be doing this. At some point, benchmark the original without having a local copy.
            if (System.IO.File.Exists(file.Path))
            {
                filehasher.Initialize();
                try
                {
                    using var f = new System.IO.FileStream(file.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                    var buffer = new byte[options.Blocksize];
                    long bytes_read = 0;
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        var read = await f.ReadAsync(buffer, 0, (int)blocks[i].BlockSize);
                        if (read == blocks[i].BlockSize)
                        {
                            filehasher.TransformBlock(buffer, 0, read, buffer, 0);
                            var blockhash = Convert.ToBase64String(blockhasher.ComputeHash(buffer, 0, read));
                            if (blockhash != blocks[i].BlockHash)
                            {
                                missing_blocks.Add(blocks[i]);
                            }
                            else
                            {
                                bytes_read += read;
                            }
                        }
                        else
                        {
                            missing_blocks.Add(blocks[i]);
                            if (f.Position == f.Length)
                            {
                                missing_blocks.AddRange(blocks.Skip(i + 1));
                                break;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    lock (results)
                    {
                        results.BrokenLocalFiles.Add(file.Path);
                    }
                }

                if (missing_blocks.Count == 0)
                {
                    filehasher.TransformFinalBlock([], 0, 0);
                    if (Convert.ToBase64String(filehasher.Hash) == file.Hash)
                    {
                        FileInfo fi = new FileInfo(file.Path);
                        if (file.Length < fi.Length)
                        {
                            if (options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have truncated ""{file.Path}"" from {fi.Length} to {file.Length}");
                            }
                            else
                            {
                                // Reopen file with write permission
                                fi.IsReadOnly = false; // The metadata handler will revert this back later.
                                using var f = new System.IO.FileStream(file.Path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);
                                f.SetLength(file.Length);
                            }
                        }
                    }
                }
            }
            else
            {
                missing_blocks.AddRange(blocks);
            }

            return missing_blocks;
        }

        private static async Task<(long, List<BlockRequest>)> VerifyLocalBlocks(LocalRestoreDatabase.IFileToRestore file, List<BlockRequest> blocks, long total_blocks, System.Security.Cryptography.HashAlgorithm filehasher, System.Security.Cryptography.HashAlgorithm blockhasher, Options options, RestoreResults results)
        {
            // Check if the file exists
            List<BlockRequest> missing_blocks = [];
            // TODO It should check both the target path and the original path, as I think the original solution might be doing this. At some point, benchmark the original without having a local copy.
            if (System.IO.File.Exists(file.Name))
            {
                filehasher.Initialize();
                using var f_original = new System.IO.FileStream(file.Name, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                using var f_target = new System.IO.FileStream(file.Path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite);
                var buffer = new byte[options.Blocksize];
                long bytes_read = 0;
                long bytes_written = 0;

                int j = 0;
                for (long i = 0; i < total_blocks; i++)
                {
                    int read;
                    if (j < blocks.Count && blocks[j].BlockOffset == i)
                    {
                        try
                        {
                            f_original.Seek(i * (long)options.Blocksize, System.IO.SeekOrigin.Begin);
                            read = await f_original.ReadAsync(buffer, 0, (int) blocks[j].BlockSize);
                        }
                        catch (Exception)
                        {
                            lock (results)
                            {
                                results.BrokenLocalFiles.Add(file.Path);
                            }
                            throw;
                        }

                        if (read == blocks[j].BlockSize)
                        {
                            var blockhash = Convert.ToBase64String(blockhasher.ComputeHash(buffer, 0, read));
                            if (blockhash != blocks[j].BlockHash)
                            {
                                missing_blocks.Add(blocks[j]);
                            }
                            else
                            {
                                try
                                {
                                    f_target.Seek(blocks[j].BlockOffset * (long)options.Blocksize, System.IO.SeekOrigin.Begin);
                                    await f_target.WriteAsync(buffer, 0, read);
                                }
                                catch (Exception)
                                {
                                    lock (results)
                                    {
                                        results.BrokenLocalFiles.Add(file.Path);
                                    }
                                    throw;
                                }
                                bytes_read += read;
                                bytes_written += read;
                            }
                        }
                        else
                        {
                            missing_blocks.Add(blocks[j]);
                            if (f_original.Position == f_original.Length)
                            {
                                missing_blocks.AddRange(blocks.Skip(j + 1));
                                break;
                            }
                        }

                        j++;
                    }
                    else
                    {
                        try
                        {
                            f_target.Seek(i * (long)options.Blocksize, System.IO.SeekOrigin.Begin);
                            read = await f_target.ReadAsync(buffer, 0, options.Blocksize);
                        }
                        catch (Exception)
                        {
                            lock (results)
                            {
                                results.BrokenLocalFiles.Add(file.Path);
                            }
                            throw;
                        }
                    }
                    filehasher.TransformBlock(buffer, 0, read, buffer, 0);
                }

                if (missing_blocks.Count == 0)
                {
                    filehasher.TransformFinalBlock([], 0, 0);
                    if (Convert.ToBase64String(filehasher.Hash) == file.Hash)
                    {
                        if (file.Length < f_target.Length)
                        {
                            if (options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have truncated ""{file.Path}"" from {f_target.Length} to {file.Length}");
                            }
                            else
                            {
                                try
                                {
                                    f_target.SetLength(file.Length);
                                }
                                catch (Exception)
                                {
                                    lock (results)
                                    {
                                        results.BrokenLocalFiles.Add(file.Path);
                                    }
                                    throw;
                                }
                            }
                        }
                    }
                }
                return (bytes_written, missing_blocks);
            }
            else
            {
                return (0, blocks);
            }
        }
    }
}