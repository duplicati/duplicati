// Copyright (C) 2025, The Duplicati Team
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
    /// If enabled, it also restores the metadata for the file.
    /// </summary>
    internal class FileProcessor
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<FileProcessor>();

        /// <summary>
        /// Runs the file processor process that restores the files that need to be restored.
        /// </summary>
        /// <param name="channels">The named channels for the restore operation.</param>
        /// <param name="db">The restore database, which is queried for blocks, corresponding volumes and metadata for the files.</param>
        /// <param name="block_request">The channel to request blocks from the block manager.</param>
        /// <param name="block_response">The channel to receive blocks from the block manager.</param>
        /// <param name="options">The restore options.</param>
        /// <param name="results">The restore results.</param>
        public static Task Run(Channels channels, LocalRestoreDatabase db, IChannel<BlockRequest> block_request, IChannel<byte[]> block_response, Options options, RestoreResults results)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.FilesToRestore.AsRead()
            },
            async self =>
            {
                Stopwatch sw_file = options.InternalProfiling ? new() : null;
                Stopwatch sw_block = options.InternalProfiling ? new() : null;
                Stopwatch sw_meta = options.InternalProfiling ? new() : null;
                Stopwatch sw_req = options.InternalProfiling ? new() : null;
                Stopwatch sw_resp = options.InternalProfiling ? new() : null;
                Stopwatch sw_work = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_verify_target = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_retarget = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_notify_local = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_verify_local = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_empty_file = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_hash = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_read = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_write = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_results = options.InternalProfiling ? new() : null;
                Stopwatch sw_work_meta = options.InternalProfiling ? new() : null;

                try
                {
                    using var filehasher = HashFactory.CreateHasher(options.FileHashAlgorithm);
                    using var blockhasher = HashFactory.CreateHasher(options.BlockHashAlgorithm);
                    var buffer = new byte[options.Blocksize];

                    while (true)
                    {
                        // Get the next file to restore.
                        sw_file?.Start();
                        var file = await self.Input.ReadAsync().ConfigureAwait(false);
                        sw_file?.Stop();

                        // Get information about the blocks for the file
                        // TODO rather than keeping all of the blocks in memory, we could do a single pass over the blocks using a cursor, only keeping the relevant block requests in memory. Maybe even only a single block request at a time.
                        sw_block?.Start();
                        var blocks = db.GetBlocksFromFile(file.BlocksetID).ToArray();
                        sw_block?.Stop();

                        sw_work_verify_target?.Start();
                        // Verify the target file blocks that may already exist.
                        var (bytes_verified, missing_blocks, verified_blocks) = await VerifyTargetBlocks(file, blocks, filehasher, blockhasher, buffer, options, results).ConfigureAwait(false);
                        long bytes_written = 0;
                        sw_work_verify_target?.Stop();
                        if (blocks.Length != missing_blocks.Count + verified_blocks.Count)
                        {
                            Logging.Log.WriteErrorMessage(LOGTAG, "BlockCountMismatch", null, $"Block count mismatch for {file.TargetPath} - expected: {blocks.Length}, actual: {missing_blocks.Count + verified_blocks.Count}");
                            continue;
                        }

                        sw_work_retarget?.Start();
                        // Check if the target file needs to be retargeted
                        if (missing_blocks.Count > 0 && !options.Overwrite && SystemIO.IO_OS.FileExists(file.TargetPath))
                        {
                            var new_name = GenerateNewName(file, db, filehasher);
                            if (SystemIO.IO_OS.FileExists(new_name))
                            {
                                // The file already exists, which it only does when it matches the target hash. So we can skip the file.
                                Logging.Log.WriteInformationMessage(LOGTAG, "FileAlreadyExists", null, $"File {file.TargetPath} already exists and matches the target hash as a copy: {new_name}");
                                missing_blocks.Clear();
                            }
                            else
                            {
                                Logging.Log.WriteVerboseMessage(LOGTAG, "RetargetingFile", "Retargeting file {0} to {1}", file.TargetPath, new_name);
                                var new_file = new FileRequest(file.ID, file.OriginalPath, new_name, file.Hash, file.Length, file.BlocksetID);
                                if (options.UseLocalBlocks)
                                {
                                    if (options.Dryrun)
                                    {
                                        Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have copied {verified_blocks.Count} blocks ({verified_blocks.Count * options.Blocksize} bytes) from ""{file.TargetPath}"" to ""{new_file.TargetPath}""");
                                    }
                                    else
                                    {
                                        await CopyOldTargetBlocksToNewTarget(file, new_file, buffer, verified_blocks).ConfigureAwait(false);
                                    }
                                }
                                else
                                {
                                    verified_blocks.Clear();
                                    missing_blocks = [.. blocks.Select(x => { x.CacheDecrEvict = false; return x; })];
                                }
                                file = new_file;
                            }
                        }
                        sw_work_retarget?.Stop();

                        sw_work_notify_local?.Start();
                        // Notify to the cache that we use local blocks.
                        foreach (var block in verified_blocks)
                        {
                            block.CacheDecrEvict = true;
                            sw_req?.Start();
                            await block_request.WriteAsync(block).ConfigureAwait(false);
                            sw_req?.Stop();
                        }
                        sw_work_notify_local?.Stop();

                        sw_work_verify_local?.Start();
                        if (missing_blocks.Count > 0 && options.UseLocalBlocks)
                        {
                            // Verify the local blocks at the original restore path that may be used to restore the file.
                            (bytes_written, missing_blocks) = await VerifyLocalBlocks(file, missing_blocks, blocks.Length, filehasher, blockhasher, buffer, options, results, block_request).ConfigureAwait(false);
                        }
                        sw_work_verify_local?.Stop();

                        bool empty_file_or_symlink = false;
                        if (file.BlocksetID != LocalDatabase.SYMLINK_BLOCKSET_ID && (blocks.Length == 0 || (blocks.Length == 1 && blocks[0].BlockSize == 0)))
                        {
                            sw_work_empty_file?.Start();
                            empty_file_or_symlink = true;
                            if (options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have created empty file ""{file.TargetPath}""");
                            }
                            else
                            {
                                var foldername = SystemIO.IO_OS.PathGetDirectoryName(file.TargetPath);
                                if (!SystemIO.IO_OS.DirectoryExists(foldername))
                                {
                                    SystemIO.IO_OS.DirectoryCreate(foldername);
                                    Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, @$"Creating missing folder ""{foldername}"" for file ""{file.TargetPath}""");
                                }

                                // Create an empty file, or truncate to 0
                                using var fs = SystemIO.IO_OS.FileOpenWrite(file.TargetPath);
                                fs.SetLength(0);
                                if (missing_blocks.Count != 0)
                                {
                                    blocks[0].CacheDecrEvict = true;
                                    await block_request.WriteAsync(blocks[0]).ConfigureAwait(false);
                                }
                            }
                            sw_work_empty_file?.Stop();
                        }
                        else if (missing_blocks.Count > 0)
                        {
                            if (missing_blocks.Any(x => x.VolumeID < 0))
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "NegativeVolumeID", null, $"{file.TargetPath} has a negative volume ID, skipping");
                                continue;
                            }

                            sw_work_hash?.Start();
                            filehasher.Initialize();
                            sw_work_hash?.Stop();

                            FileStream fs = null;
                            try
                            {
                                // Open the target file
                                if (options.Dryrun)
                                {
                                    // If dryrun, open the file for read only to verify the file hash.
                                    if (File.Exists(file.TargetPath))
                                    {
                                        fs = SystemIO.IO_OS.FileOpenRead(file.TargetPath);
                                    }
                                    else
                                    {
                                        Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Tried opening ""{file.TargetPath}"" for reading, but it doesn't exist.");
                                    }
                                }
                                else
                                {
                                    var foldername = SystemIO.IO_OS.PathGetDirectoryName(file.TargetPath);
                                    if (!SystemIO.IO_OS.DirectoryExists(foldername))
                                    {
                                        SystemIO.IO_OS.DirectoryCreate(foldername);
                                        Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, @$"Creating missing folder ""{foldername}"" for file ""{file.TargetPath}""");
                                    }
                                    fs = SystemIO.IO_OS.FileOpenReadWrite(file.TargetPath);
                                }

                                sw_req?.Start();
                                // Burst the block requests to speed up the restore
                                int burst = Channels.BufferSize;
                                int j = 0;
                                for (int i = 0; i < (int)Math.Min(missing_blocks.Count, burst); i++)
                                {
                                    await block_request.WriteAsync(missing_blocks[i]).ConfigureAwait(false);
                                }
                                sw_req?.Stop();

                                if (!options.Dryrun && options.RestorePreAllocate)
                                {
                                    // Preallocate the file size to avoid fragmentation / help the operating system / filesystem.
                                    fs.SetLength(file.Length);
                                }

                                for (int i = 0; i < blocks.Length; i++)
                                {
                                    // Each response block is not verified against the block hash, as the processes downloading the blocks should do that.
                                    if (j < missing_blocks.Count && missing_blocks[j].BlockOffset == i)
                                    {
                                        // Read the block from the response and issue a new request, if more blocks are missing
                                        sw_resp?.Start();
                                        var data = await block_response.ReadAsync().ConfigureAwait(false);
                                        sw_resp?.Stop();

                                        sw_req?.Start();
                                        if (j < missing_blocks.Count - burst)
                                        {
                                            await block_request.WriteAsync(missing_blocks[j + burst]).ConfigureAwait(false);
                                        }
                                        sw_req?.Stop();

                                        // Hash the block to verify the file hash
                                        sw_work_hash?.Start();
                                        filehasher.TransformBlock(data, 0, (int)blocks[i].BlockSize, data, 0);
                                        sw_work_hash?.Stop();
                                        if (options.Dryrun)
                                        {
                                            // Simulate writing the block
                                            fs?.Seek(fs.Position + blocks[i].BlockSize, SeekOrigin.Begin);
                                        }
                                        else
                                        {
                                            // Write the block to the file
                                            sw_work_write?.Start();
                                            await fs.WriteAsync(data.AsMemory(0, (int)blocks[i].BlockSize)).ConfigureAwait(false);
                                            sw_work_write?.Stop();
                                        }

                                        // Keep track of metrics
                                        bytes_written += blocks[i].BlockSize;
                                        j++;
                                        sw_req?.Start();
                                        blocks[i].CacheDecrEvict = true;
                                        await block_request.WriteAsync(blocks[i]).ConfigureAwait(false);
                                        sw_req?.Stop();
                                    }
                                    else
                                    {
                                        // Not a missing block, so read from the file
                                        sw_work_read?.Start();
                                        var read = await fs?.ReadAsync(buffer, 0, buffer.Length);
                                        sw_work_read?.Stop();
                                        sw_work_hash?.Start();
                                        filehasher.TransformBlock(buffer, 0, read, buffer, 0);
                                        sw_work_hash?.Stop();
                                    }
                                }

                                if (options.Dryrun)
                                {
                                    Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have restored {bytes_written} bytes of ""{file.TargetPath}""");
                                }

                                // Verify the file hash
                                sw_work_hash?.Start();
                                filehasher.TransformFinalBlock([], 0, 0);
                                sw_work_hash?.Stop();

                                sw_work?.Start();
                                if (Convert.ToBase64String(filehasher.Hash) != file.Hash)
                                {
                                    Logging.Log.WriteErrorMessage(LOGTAG, "FileHashMismatch", null, $"File hash mismatch for {file.TargetPath} - expected: {file.Hash}, actual: {Convert.ToBase64String(filehasher.Hash)}");
                                    throw new Exception("File hash mismatch");
                                }

                                // Truncate the file if it is larger than the expected size.
                                if (fs?.Length > file.Length)
                                {
                                    if (options.Dryrun)
                                    {
                                        Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have truncated ""{file.TargetPath}"" from {fs.Length} to {file.Length}");
                                    }
                                    else
                                    {
                                        fs.SetLength(file.Length);
                                    }
                                }
                                sw_work?.Stop();
                            }
                            catch (Exception)
                            {
                                lock (results)
                                {
                                    results.BrokenLocalFiles.Add(file.TargetPath);
                                }
                                block_request.Retire();
                                block_response.Retire();
                                throw;
                            }
                            finally
                            {
                                fs?.Dispose();
                            }
                        }

                        if (!options.SkipMetadata)
                        {
                            empty_file_or_symlink |= await RestoreMetadata(db, file, block_request, block_response, options, sw_meta, sw_work_meta, sw_req, sw_resp).ConfigureAwait(false);
                            sw_work_meta?.Stop();
                        }

                        // TODO legacy restore doesn't count metadata restore as a restored file.

                        sw_work_results?.Start();
                        if (empty_file_or_symlink || bytes_written > 0)
                        {
                            // Keep track of the restored files and their sizes
                            lock (results)
                            {
                                results.RestoredFiles++;
                                results.SizeOfRestoredFiles += bytes_written;
                            }
                        }
                        sw_work_results?.Stop();

                        Logging.Log.WriteVerboseMessage(LOGTAG, "RestoredFile", "Restored file {0}", file.TargetPath);
                    }
                }
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "File processor retired");

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"File: {sw_file.ElapsedMilliseconds}ms, Block: {sw_block.ElapsedMilliseconds}ms, Meta: {sw_meta.ElapsedMilliseconds}ms, Req: {sw_req.ElapsedMilliseconds}ms, Resp: {sw_resp.ElapsedMilliseconds}ms, Work: {sw_work.ElapsedMilliseconds}ms, VerifyTarget: {sw_work_verify_target.ElapsedMilliseconds}ms, Retarget: {sw_work_retarget.ElapsedMilliseconds}ms, NotifyLocal: {sw_work_notify_local.ElapsedMilliseconds}ms, VerifyLocal: {sw_work_verify_local.ElapsedMilliseconds}ms, EmptyFile: {sw_work_empty_file.ElapsedMilliseconds}ms, Hash: {sw_work_hash.ElapsedMilliseconds}ms, Read: {sw_work_read.ElapsedMilliseconds}ms, Write: {sw_work_write.ElapsedMilliseconds}ms, Results: {sw_work_results.ElapsedMilliseconds}ms, Meta: {sw_work_meta.ElapsedMilliseconds}ms");
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "FileProcessingError", ex, "Error during file processing");
                    throw;
                }
                finally
                {
                    block_request.Retire();
                    block_response.Retire();
                }
            });
        }

        /// <summary>
        /// Copies the blocks that were verified in the old target to the new target file.
        /// </summary>
        /// <param name="old_file">The old target file.</param>
        /// <param name="new_file">The new target file.</param>
        /// <param name="verified_blocks">The blocks in the old file that were verified.</param>
        private static async Task CopyOldTargetBlocksToNewTarget(FileRequest old_file, FileRequest new_file, byte[] buffer, List<BlockRequest> verified_blocks)
        {
            using var fs_old = SystemIO.IO_OS.FileOpenRead(old_file.TargetPath);
            using var fs_new = SystemIO.IO_OS.FileOpenWrite(new_file.TargetPath);

            foreach (var block in verified_blocks)
            {
                fs_old.Seek(block.BlockOffset * block.BlockSize, SeekOrigin.Begin);
                fs_new.Seek(block.BlockOffset * block.BlockSize, SeekOrigin.Begin);

                await fs_old.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                await fs_new.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false); ;
            }
        }

        /// <summary>
        /// Generates a new name for the target file. It starts by appending
        /// the restore time to the file name. If that name is also already
        /// taken, it appends a number to the name. It keeps incrementing the
        /// number until it finds a name that is not taken. If it encounters a
        /// file with the same name and the correct hash, it will retarget the
        /// filename to that file, assuming that the file is the correct one.
        /// </summary>
        /// <param name="request">The original target file.</param>
        /// <param name="database">The restore database.</param>
        /// <param name="filehasher">The file hasher used to verify whether any of the </param>
        /// <returns>The new filename. If the file already exists, its file hash matches the target hash.</returns>
        private static string GenerateNewName(FileRequest request, LocalRestoreDatabase database, System.Security.Cryptography.HashAlgorithm filehasher)
        {
            var ext = SystemIO.IO_OS.PathGetExtension(request.TargetPath) ?? "";
            if (!string.IsNullOrEmpty(ext) && !ext.StartsWith(".", StringComparison.Ordinal))
                ext = "." + ext;

            // First we try with a simple date append, assuming that there are not many conflicts there
            var newname = SystemIO.IO_OS.PathChangeExtension(request.TargetPath, null) + "." + database.RestoreTime.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var tr = newname + ext;
            var c = 0;
            var max_tries = 100;
            while (SystemIO.IO_OS.FileExists(tr) && c < max_tries)
            {
                try
                {
                    // If we have a file with the correct name,
                    // it is most likely the file we want
                    filehasher.Initialize();

                    string key;
                    using (var file = SystemIO.IO_OS.FileOpenRead(tr))
                        key = Convert.ToBase64String(filehasher.ComputeHash(file));

                    if (key == request.Hash)
                    {
                        //TODO: Also needs metadata check to make correct decision.
                        //      We stick to the policy to restore metadata in place, if data ok. So, metadata block may be restored.
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FailedToReadRestoreTarget", ex, "Failed to read candidate restore target {0}", tr);
                }
                tr = newname + " (" + (c++).ToString() + ")" + ext;
            }

            if (c >= max_tries)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "TooManyConflicts", null, "Too many conflicts when trying to find a new name for the target file");
                throw new Exception("Too many conflicts when trying to find a new name for the target file");
            }

            newname = tr;

            return newname;
        }

        /// <summary>
        /// Restores the metadata for a file.
        /// </summary>
        /// <param name="cmd">The command to execute to retrieve the metadata blocks.</param>
        /// <param name="file">The file to restore.</param>
        /// <param name="block_request">The channel to request blocks from the block manager.</param>
        /// <param name="block_response">The channel to receive blocks from the block manager.</param>
        /// <param name="options">The restore options.</param>
        /// <param name="sw_meta">The stopwatch for internal profiling of the metadata processing.</param>
        /// <param name="sw_work">The stopwatch for internal profiling of the general processing.</param>
        /// <param name="sw_req">The stopwatch for internal profiling of the block requests.</param>
        /// <param name="sw_resp">The stopwatch for internal profiling of the block responses.</param>
        private static async Task<bool> RestoreMetadata(LocalRestoreDatabase db, FileRequest file, IChannel<BlockRequest> block_request, IChannel<byte[]> block_response, Options options, Stopwatch sw_meta, Stopwatch sw_work, Stopwatch sw_req, Stopwatch sw_resp)
        {
            sw_meta?.Start();
            var blocks = db.GetMetadataBlocksFromFile(file.ID);
            sw_meta?.Stop();

            using var ms = new MemoryStream();

            foreach (var block in blocks)
            {
                sw_work?.Stop();
                sw_req?.Start();
                await block_request.WriteAsync(block).ConfigureAwait(false);
                sw_req?.Stop();

                sw_resp?.Start();
                var data = await block_response.ReadAsync().ConfigureAwait(false);
                sw_resp?.Stop();

                sw_work?.Start();
                ms.Write(data, 0, (int)block.BlockSize);
                sw_work?.Stop();

                sw_req?.Start();
                block.CacheDecrEvict = true;
                await block_request.WriteAsync(block).ConfigureAwait(false);
                sw_req?.Stop();

                sw_work?.Start();
            }
            ms.Seek(0, SeekOrigin.Begin);

            return RestoreHandler.ApplyMetadata(file.TargetPath, ms, options.RestorePermissions, options.RestoreSymlinkMetadata, options.Dryrun);
        }

        /// <summary>
        /// Verifies the target blocks of a file that may already exist.
        /// </summary>
        /// <param name="file">The target file.</param>
        /// <param name="blocks">The metadata about the blocks that make up the file.</param>
        /// <param name="filehasher">A hasher for the file.</param>
        /// <param name="blockhasher">A hasher for a data block.</param>
        /// <param name="buffer">A buffer to read and write data blocks.</param>
        /// <param name="options">The Duplicati configuration options.</param>
        /// <param name="results">The restoration results.</param>
        /// <returns>An awaitable `Task`, which returns a collection of data blocks that are missing.</returns>
        private static async Task<(long, List<BlockRequest>, List<BlockRequest>)> VerifyTargetBlocks(FileRequest file, BlockRequest[] blocks, System.Security.Cryptography.HashAlgorithm filehasher, System.Security.Cryptography.HashAlgorithm blockhasher, byte[] buffer, Options options, RestoreResults results)
        {
            long bytes_read = 0;
            List<BlockRequest> missing_blocks = [];
            List<BlockRequest> verified_blocks = [];

            // Check if the file exists
            if (File.Exists(file.TargetPath))
            {
                filehasher.Initialize();
                try
                {
                    using var f = SystemIO.IO_OS.FileOpenRead(file.TargetPath);
                    for (int i = 0; i < blocks.Length; i++)
                    {
                        var read = await f.ReadAsync(buffer, 0, (int)blocks[i].BlockSize).ConfigureAwait(false);
                        if (read == blocks[i].BlockSize)
                        {
                            // Block is present
                            filehasher.TransformBlock(buffer, 0, read, buffer, 0);
                            var blockhash = Convert.ToBase64String(blockhasher.ComputeHash(buffer, 0, read));
                            if (blockhash == blocks[i].BlockHash)
                            {
                                // Block matches
                                bytes_read += read;
                                verified_blocks.Add(blocks[i]);
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
                        results.BrokenLocalFiles.Add(file.TargetPath);
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
                        FileInfo fi = new(file.TargetPath);
                        if (file.Length < fi.Length)
                        {
                            if (options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have truncated ""{file.TargetPath}"" from {fi.Length} to {file.Length}");
                            }
                            else
                            {
                                // Reopen file with write permission
                                fi.IsReadOnly = false; // The metadata handler will revert this back later.
                                using var f = SystemIO.IO_OS.FileOpenWrite(file.TargetPath);
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

            return (bytes_read, missing_blocks, verified_blocks);
        }

        /// <summary>
        /// Verifies the local blocks at the original restore path that may be used to restore the file.
        /// </summary>
        /// <param name="file">The file to restore. Contains both the target and original paths.</param>
        /// <param name="blocks">The collection of blocks that are currently missing.</param>
        /// <param name="total_blocks">The total number of blocks for the file.</param>
        /// <param name="filehasher">A hasher for the file.</param>
        /// <param name="blockhasher">A hasher for the data block.</param>
        /// <param name="buffer">A buffer to read and write data blocks.</param>
        /// <param name="options">The Duplicati configuration options.</param>
        /// <param name="results">The restoration results.</param>
        /// <param name="block_request">The channel to request blocks from the block manager. Used to inform the block manager which blocks are already present.</param>
        /// <returns>An awaitable `Task`, which returns a collection of data blocks that are missing.</returns>
        private static async Task<(long, List<BlockRequest>)> VerifyLocalBlocks(FileRequest file, List<BlockRequest> blocks, long total_blocks, System.Security.Cryptography.HashAlgorithm filehasher, System.Security.Cryptography.HashAlgorithm blockhasher, byte[] buffer, Options options, RestoreResults results, IChannel<BlockRequest> block_request)
        {
            if (file.TargetPath == file.OriginalPath)
            {
                // The original file is the same as the target file, so no new blocks can be used.
                return (0, blocks);
            }

            List<BlockRequest> missing_blocks = [];
            List<BlockRequest> verified_blocks = [];

            // Check if the file exists
            if (File.Exists(file.OriginalPath))
            {
                filehasher.Initialize();

                // Open both files, as the target file is still being read to produce the overall file hash, if all the blocks are present across both the target and original files.
                using var f_original = SystemIO.IO_OS.FileOpenRead(file.OriginalPath);
                using var f_target = options.Dryrun ?
                    (SystemIO.IO_OS.FileExists(file.TargetPath) ?
                        SystemIO.IO_OS.FileOpenRead(file.TargetPath) :
                        null) :
                    SystemIO.IO_OS.FileOpenReadWrite(file.TargetPath);
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
                            f_original.Seek(i * options.Blocksize, SeekOrigin.Begin);
                            read = await f_original.ReadAsync(buffer, 0, (int)blocks[j].BlockSize).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            lock (results)
                            {
                                results.BrokenLocalFiles.Add(file.TargetPath);
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
                                if (!options.Dryrun)
                                {
                                    try
                                    {
                                        f_target?.Seek(blocks[j].BlockOffset * options.Blocksize, SeekOrigin.Begin);
                                        await f_target?.WriteAsync(buffer, 0, read);
                                    }
                                    catch (Exception)
                                    {
                                        lock (results)
                                        {
                                            results.BrokenLocalFiles.Add(file.TargetPath);
                                        }
                                        throw;
                                    }
                                }
                                bytes_read += read;
                                bytes_written += read;
                                blocks[j].CacheDecrEvict = true;
                                await block_request.WriteAsync(blocks[j]).ConfigureAwait(false);
                                verified_blocks.Add(blocks[j]);
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
                            f_target?.Seek(i * options.Blocksize, SeekOrigin.Begin);
                            read = await f_target?.ReadAsync(buffer, 0, options.Blocksize);
                        }
                        catch (Exception)
                        {
                            lock (results)
                            {
                                results.BrokenLocalFiles.Add(file.TargetPath);
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
                        if (file.Length < f_target?.Length)
                        {
                            if (options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have truncated ""{file.TargetPath}"" from {f_target.Length} to {file.Length}");
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
                                        results.BrokenLocalFiles.Add(file.TargetPath);
                                    }
                                    throw;
                                }
                            }
                        }
                    }
                    else
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "FileHashMismatch", null, $"File hash mismatch for {file.TargetPath} - expected: {file.Hash}, actual: {Convert.ToBase64String(filehasher.Hash)}");
                        lock (results)
                        {
                            results.BrokenLocalFiles.Add(file.TargetPath);
                        }
                    }
                }

                if (options.Dryrun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunRestore", @$"Would have restored {verified_blocks.Count} blocks ({bytes_written} bytes) from ""{file.OriginalPath}"" to ""{file.TargetPath}""");
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