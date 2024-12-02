// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that handles each file that needs to be restored. It starts by
    /// identifying the blocks that need to be restored. Then it verifies the
    /// which of the target file blocks are missing. Then it checks whether it
    /// can use local blocks to restore the file. Finally, it restores the file
    /// by downloading the missing blocks and writing them to the target file.
    /// It also verifies the file hash and truncates the file if necessary.
    /// </summary>
    internal class FileProcessor
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<FileProcessor>();

        public static Task Run(LocalRestoreDatabase db, IChannel<BlockRequest> block_request, IChannel<byte[]> block_response, RestoreHandlerMetadataStorage metadatastorage, Options options, RestoreResults results)
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

                Stopwatch sw_file  = options.InternalProfiling ? new () : null;
                Stopwatch sw_block = options.InternalProfiling ? new () : null;
                Stopwatch sw_meta  = options.InternalProfiling ? new () : null;
                Stopwatch sw_req   = options.InternalProfiling ? new () : null;
                Stopwatch sw_resp  = options.InternalProfiling ? new () : null;
                Stopwatch sw_work  = options.InternalProfiling ? new () : null;

                try
                {
                    using var filehasher = HashFactory.CreateHasher(options.FileHashAlgorithm);
                    using var blockhasher = HashFactory.CreateHasher(options.BlockHashAlgorithm);
                    using var findBlocksCmd = db.Connection.CreateCommand();
                    findBlocksCmd.CommandText = @$"
                        SELECT Block.ID, Block.Hash, Block.Size, Block.VolumeID
                        FROM BlocksetEntry INNER JOIN Block
                        ON BlocksetEntry.BlockID = Block.ID
                        WHERE BlocksetEntry.BlocksetID = ?";
                    findBlocksCmd.AddParameter();

                    var findMetaBlocksCmd = db.Connection.CreateCommand();
                    findMetaBlocksCmd.CommandText = $@"
                        SELECT Block.ID, Block.Hash, Block.Size, Block.VolumeID
                        FROM ""{db.m_tempfiletable}""
                        INNER JOIN Metadataset ON ""{db.m_tempfiletable}"".MetadataID = Metadataset.ID
                        INNER JOIN BlocksetEntry ON Metadataset.BlocksetID = BlocksetEntry.BlocksetID
                        INNER JOIN Block ON BlocksetEntry.BlockID = Block.ID
                        WHERE ""{db.m_tempfiletable}"".ID = ?
                    ";
                    findMetaBlocksCmd.AddParameter();

                    while (true)
                    {
                        // Get the next file to restore.
                        sw_file?.Start();
                        var file = await self.Input.ReadAsync();
                        sw_file?.Stop();

                        // Get information about the blocks for the file
                        sw_block?.Start();
                        findBlocksCmd.SetParameterValue(0, file.BlocksetID);
                        var blocks = findBlocksCmd.ExecuteReaderEnumerable()
                            .Select((b, i) =>
                                new BlockRequest(b.GetInt64(0), i, b.GetString(1), b.GetInt64(2), b.GetInt64(3), false)
                            )
                            .ToArray();
                        sw_block?.Stop();

                        sw_work?.Start();
                        // Verify the target file blocks that may already exist.
                        var missing_blocks = await VerifyTargetBlocks(file, blocks, filehasher, blockhasher, options, results, block_request);

                        long bytes_written = 0;

                        if (missing_blocks.Count > 0 && options.UseLocalBlocks && missing_blocks.Count > 0)
                        {
                            // Verify the local blocks at the original restore path that may be used to restore the file.
                            var (bw, new_missing_blocks) = await VerifyLocalBlocks(file, missing_blocks, blocks.Length, filehasher, blockhasher, options, results, block_request);
                            bytes_written = bw;
                            missing_blocks = new_missing_blocks;
                        }

                        if (file.BlocksetID != LocalDatabase.SYMLINK_BLOCKSET_ID && (blocks.Length == 0 || (blocks.Length == 1 && blocks[0].BlockSize == 0)))
                        {
                            if (options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have created empty file ""{file.Path}""");
                            }
                            else
                            {
                                var foldername = SystemIO.IO_OS.PathGetDirectoryName(file.Path);
                                if (!System.IO.Directory.Exists(foldername))
                                {
                                    System.IO.Directory.CreateDirectory(foldername);
                                }

                                // Create an empty file, or truncate to 0
                                using var fs = new System.IO.FileStream(file.Path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.None);
                                fs.SetLength(0);
                                if (missing_blocks.Count != 0)
                                {
                                    blocks[0].CacheDecrEvict = true;
                                    await block_request.WriteAsync(blocks[0]);
                                }
                            }
                        }
                        else if (missing_blocks.Count > 0)
                        {
                            if (missing_blocks.Any(x => x.VolumeID < 0))
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "NegativeVolumeID", null, $"{file.Path} has a negative volume ID, skipping");
                                continue;
                            }

                            filehasher.Initialize();

                            FileStream fs = null;
                            try
                            {
                                // Open the target file
                                if (options.Dryrun)
                                {
                                    // If dryrun, open the file for read only to verify the file hash.
                                    if (System.IO.File.Exists(file.Path))
                                    {
                                        fs = new System.IO.FileStream(file.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                                    }
                                }
                                else
                                {
                                    fs = new System.IO.FileStream(file.Path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
                                }

                                sw_work?.Stop();
                                sw_req?.Start();
                                // Burst the block requests to speed up the restore
                                int burst = Channels.bufferSize;
                                int j = 0;
                                for (int i = 0; i < (int) Math.Min(missing_blocks.Count, burst); i++)
                                {
                                    await block_request.WriteAsync(missing_blocks[i]);
                                }
                                sw_req?.Stop();
                                sw_work?.Start();
                                for (int i = 0; i < blocks.Length; i++)
                                {
                                    // Each response block is not verified against the block hash, as the processes downloading the blocks should do that.
                                    if (j < missing_blocks.Count && missing_blocks[j].BlockOffset == i)
                                    {
                                        // Read the block from the response and issue a new request, if more blocks are missing
                                        sw_work?.Stop();
                                        sw_resp?.Start();
                                        var data = await block_response.ReadAsync();
                                        sw_resp?.Stop();
                                        sw_req?.Start();
                                        if (j < missing_blocks.Count - burst)
                                        {
                                            await block_request.WriteAsync(missing_blocks[j + burst]);
                                        }
                                        sw_req?.Stop();
                                        sw_work?.Start();

                                        // Hash the block to verify the file hash
                                        filehasher.TransformBlock(data, 0, data.Length, data, 0);
                                        if (options.Dryrun)
                                        {
                                            // Simulate writing the block
                                            fs.Seek(fs.Position + blocks[i].BlockSize, System.IO.SeekOrigin.Begin);
                                        }
                                        else
                                        {
                                            // Write the block to the file
                                            await fs.WriteAsync(data);
                                        }

                                        // Keep track of metrics
                                        bytes_written += data.Length;
                                        j++;
                                    }
                                    else
                                    {
                                        // No more blocks are missing, so read the rest of the blocks to verify the file hash.
                                        var data = new byte[blocks[i].BlockSize];
                                        var read = await fs.ReadAsync(data, 0, data.Length);
                                        filehasher.TransformBlock(data, 0, read, data, 0);
                                    }
                                }

                                if (options.Dryrun)
                                {
                                    Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have restored {bytes_written} bytes of ""{file.Path}""");
                                }

                                // Verify the file hash
                                filehasher.TransformFinalBlock([], 0, 0);
                                if (Convert.ToBase64String(filehasher.Hash) != file.Hash)
                                {
                                    Logging.Log.WriteErrorMessage(LOGTAG, "FileHashMismatch", null, $"File hash mismatch for {file.Path} - expected: {file.Hash}, actual: {Convert.ToBase64String(filehasher.Hash)}");
                                    throw new Exception("File hash mismatch");
                                }

                                // Truncate the file if it is larger than the expected size.
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
                            finally
                            {
                                fs?.Dispose();
                            }
                        }

                        if (!options.SkipMetadata) {
                            await RestoreMetadata(db, findMetaBlocksCmd, file, block_request, block_response, options, results, sw_meta, sw_work, sw_req, sw_resp);
                        }

                        // Keep track of the restored files and their sizes
                        lock (results)
                        {
                            results.RestoredFiles++;
                            results.SizeOfRestoredFiles += bytes_written;
                        }
                        sw_work?.Stop();
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "File processor retired");
                    block_request.Retire();
                    block_response.Retire();

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"File: {sw_file.ElapsedMilliseconds}ms, Block: {sw_block.ElapsedMilliseconds}ms, Meta: {sw_meta.ElapsedMilliseconds}ms, Req: {sw_req.ElapsedMilliseconds}ms, Resp: {sw_resp.ElapsedMilliseconds}ms, Work: {sw_work.ElapsedMilliseconds}ms");
                        Console.WriteLine($"File processor - File: {sw_file.ElapsedMilliseconds}ms, Block: {sw_block.ElapsedMilliseconds}ms, Meta: {sw_meta.ElapsedMilliseconds}ms, Req: {sw_req.ElapsedMilliseconds}ms, Resp: {sw_resp.ElapsedMilliseconds}ms, Work: {sw_work.ElapsedMilliseconds}ms");
                    }
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

        // TODO double check all of the docstrings
        private static async Task<bool> RestoreMetadata(LocalRestoreDatabase db, IDbCommand cmd, LocalRestoreDatabase.IFileToRestore file, IChannel<BlockRequest> block_request, IChannel<byte[]> block_response, Options options, RestoreResults results, Stopwatch sw_meta, Stopwatch sw_work, Stopwatch sw_req, Stopwatch sw_resp)
        {
            sw_work?.Stop();
            sw_meta?.Start();
            cmd.SetParameterValue(0, file.ID);
            var blocks = cmd.ExecuteReaderEnumerable()
                .Select((b, i) =>
                    new BlockRequest(b.GetInt64(0), i, b.GetString(1), b.GetInt64(2), b.GetInt64(3), false)
                )
                .ToArray();
            sw_meta?.Stop();
            sw_work?.Start();

            using var ms = new MemoryStream();

            foreach (var block in blocks)
            {
                sw_work?.Stop();
                sw_req?.Start();
                await block_request.WriteAsync(block);
                sw_req?.Stop();
                sw_resp?.Start();
                //Console.WriteLine("Reading metadata block");
                //Deadlock occurs when the print is missing - look into block manager
                var data = await block_response.ReadAsync();
                sw_resp?.Stop();
                sw_work?.Start();
                ms.Write(data, 0, data.Length);
            }
            ms.Seek(0, SeekOrigin.Begin);

            RestoreHandler.ApplyMetadata(file.Path, ms, options.RestorePermissions, options.RestoreSymlinkMetadata, options.Dryrun);

            return true;
        }

        /// <summary>
        /// Verifies the target blocks of a file that may already exist.
        /// </summary>
        /// <param name="file">The target file.</param>
        /// <param name="blocks">The metadata about the blocks that make up the file.</param>
        /// <param name="filehasher">A hasher for the file.</param>
        /// <param name="blockhasher">A hasher for a data block.</param>
        /// <param name="options">The Duplicati configuration options.</param>
        /// <param name="results">The restoration results.</param>
        /// <returns>An awaitable `Task`, which returns a collection of data blocks that are missing.</returns>
        private static async Task<List<BlockRequest>> VerifyTargetBlocks(LocalRestoreDatabase.IFileToRestore file, BlockRequest[] blocks, System.Security.Cryptography.HashAlgorithm filehasher, System.Security.Cryptography.HashAlgorithm blockhasher, Options options, RestoreResults results, IChannel<BlockRequest> block_request)
        {
            List<BlockRequest> missing_blocks = [];

            // Check if the file exists
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
                            // Block is present
                            filehasher.TransformBlock(buffer, 0, read, buffer, 0);
                            var blockhash = Convert.ToBase64String(blockhasher.ComputeHash(buffer, 0, read));
                            if (blockhash == blocks[i].BlockHash)
                            {
                                // Block matches
                                bytes_read += read;
                                blocks[i].CacheDecrEvict = true;
                                await block_request.WriteAsync(blocks[i]);
                            }
                            else
                            {
                                // Block mismatch
                                missing_blocks.Add(blocks[i]);
                            }
                        }
                        else
                        {
                            // Block is missing
                            missing_blocks.Add(blocks[i]);
                            if (f.Position == f.Length)
                            {
                                // No more file - the rest of the blocks are missing.
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

                // If all of the individual blocks have been verified.
                if (missing_blocks.Count == 0)
                {
                    // Verify the file hash
                    filehasher.TransformFinalBlock([], 0, 0);
                    if (Convert.ToBase64String(filehasher.Hash) == file.Hash)
                    {
                        // Truncate the file if it is larger than the expected size.
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
                // The file doesn't exist, so all blocks are missing.
                missing_blocks.AddRange(blocks);
            }

            return missing_blocks;
        }

        /// <summary>
        /// Verifies the local blocks at the original restore path that may be used to restore the file.
        /// </summary>
        /// <param name="file">The file to restore. Contains both the target and original paths.</param>
        /// <param name="blocks">The collection of blocks that are currently missing.</param>
        /// <param name="total_blocks">The total number of blocks for the file.</param>
        /// <param name="filehasher">A hasher for the file.</param>
        /// <param name="blockhasher">A hasher for the data block.</param>
        /// <param name="options">The Duplicati configuration options.</param>
        /// <param name="results">The restoration results.</param>
        /// <returns>An awaitable `Task`, which returns a collection of data blocks that are missing.</returns>
        private static async Task<(long, List<BlockRequest>)> VerifyLocalBlocks(LocalRestoreDatabase.IFileToRestore file, List<BlockRequest> blocks, long total_blocks, System.Security.Cryptography.HashAlgorithm filehasher, System.Security.Cryptography.HashAlgorithm blockhasher, Options options, RestoreResults results, IChannel<BlockRequest> block_request)
        {
            List<BlockRequest> missing_blocks = [];

            // Check if the file exists
            if (System.IO.File.Exists(file.Name))
            {
                filehasher.Initialize();

                // Open both files, as the target file is still being read to produce the overall file hash, if all the blocks are present across both the target and original files.
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
                        // The current block is a missing block
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
                                blocks[j].CacheDecrEvict = true;
                                await block_request.WriteAsync(blocks[j]);
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
                        // The current block is not a missing block - read from the target file.
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
                    // No more blocks are missing, so check the file hash.
                    filehasher.TransformFinalBlock([], 0, 0);
                    if (Convert.ToBase64String(filehasher.Hash) == file.Hash)
                    {
                        // Truncate the file if it is larger than the expected size.
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
                    else
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "FileHashMismatch", null, $"File hash mismatch for {file.Path} - expected: {file.Hash}, actual: {Convert.ToBase64String(filehasher.Hash)}");
                        lock (results)
                        {
                            results.BrokenLocalFiles.Add(file.Path);
                        }
                    }
                }

                return (bytes_written, missing_blocks);
            }
            else
            {
                // The original file is no longer present, so all blocks are missing.
                return (0, blocks);
            }
        }
    }

}