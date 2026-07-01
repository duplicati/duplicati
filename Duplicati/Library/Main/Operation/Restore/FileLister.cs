// Copyright (C) 2026, The Duplicati Team
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
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Common.IO;

#nullable enable

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Process that holds the files that this particular restore operation needs to restore.
    /// </summary>
    internal class FileLister
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<FileLister>();

        /// <summary>
        /// Runs the file lister process that lists the files that need to be restored
        /// and sends them to the <see cref="FileProcessor"/>.
        /// </summary>
        /// <param name="channels">The named channels for the restore operation.</param>
        /// <param name="db">The restore database, which is queried for the file list.</param>
        /// <param name="options">The restore options</param>
        /// <param name="result">The restore results</param>
        /// <param name="priorityFiles">The list of priority file names to include.</param>
        /// <param name="version">The 0-based backup version index being restored (0 = newest), reported to restore callback modules.</param>
        /// <param name="backupTimestamp">The timestamp of the backup version being restored, in UTC, reported to restore callback modules.</param>
        /// <param name="modules">The loaded generic modules, used to dispatch the bulk-restore-start callback. May be null.</param>
        public static Task RunAsync(Channels channels, LocalRestoreDatabase db, Options options, RestoreResults result, IList<string> priorityFiles, long version, DateTime backupTimestamp, IEnumerable<IGenericModule>? modules)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Output = channels.FilesToRestore.AsWrite()
            },
            async self =>
            {
                Stopwatch? sw_get_files = options.InternalProfiling ? new() : null;
                Stopwatch? sw_write_file = options.InternalProfiling ? new() : null;
                Stopwatch? sw_get_folders = options.InternalProfiling ? new() : null;
                Stopwatch? sw_write_folder = options.InternalProfiling ? new() : null;

                bool threw_exception = false;

                try
                {
                    sw_get_files?.Start();
                    // The enumerables are cast to arrays to force the query to be executed and release the database lock.
                    var files = (await db
                        .GetFilesAndSymlinksToRestoreAsync(result.TaskControl.ProgressToken)
                        .ToArrayAsync()
                        .ConfigureAwait(false))
                        .AsEnumerable();

                    result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_DownloadingRemoteFiles);
                    sw_get_files?.Stop();

                    // Separate out alternate data streams so they are restored last
                    var adsStreams = new List<FileRequest>();
                    if (SystemIO.IO_OS.SupportsAlternateDataStreams)
                    {
                        var hostFiles = new List<FileRequest>();
                        foreach (var f in files)
                            if (SystemIO.IO_OS.IsAlternateDataStream(f.TargetPath))
                            {
                                if (options.DisableAdsRestore)
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "SkipAdsRestore", "Skipping ADS restore for {0}", f.TargetPath);
                                else
                                    adsStreams.Add(f);
                            }
                            else
                                hostFiles.Add(f);

                        files = hostFiles;
                    }

                    sw_write_file?.Start();

                    // Resolve which files are priority files. A priority entry only counts
                    // if it matches at least one restorable target; entries a restore callback
                    // module added that match nothing are ignored so the priority-file counter
                    // below stays consistent with the number of priority FileRequests actually
                    // emitted. Without this, a non-matching entry would leave the non-priority
                    // FileProcessors waiting forever for a priority file that never arrives.
                    List<FileRequest> priorityFileList;
                    IEnumerable<FileRequest> remainingFiles;
                    if (priorityFiles.Count > 0)
                    {
                        var priorityFileSet = new HashSet<string>(priorityFiles, StringComparer.OrdinalIgnoreCase);
                        priorityFileList = files.Where(f => priorityFileSet.Any(pf => f.TargetPath.EndsWith(pf, StringComparison.OrdinalIgnoreCase))).ToList();
                        remainingFiles = files.Where(f => !priorityFileSet.Any(pf => f.TargetPath.EndsWith(pf, StringComparison.OrdinalIgnoreCase)));
                    }
                    else
                    {
                        priorityFileList = new List<FileRequest>();
                        remainingFiles = files;
                    }

                    // The priority-file counter must reflect the number of priority
                    // FileRequests actually emitted below, not the size of the (module-mutable)
                    // priority-files list. Resetting the completion source here (the producer)
                    // is safe: the FileProcessors only await it for non-priority files, which are
                    // emitted afterwards, and they cannot read a file until it has been written.
                    FileProcessor.priority_files_remaining = priorityFileList.Count;
                    FileProcessor.priority_files_completed = new TaskCompletionSource();

                    // When no priority files will be processed, the bulk restore starts
                    // immediately; notify restore callback modules now. When priority files
                    // exist, the FileProcessor that finishes the last one notifies the modules.
                    if (priorityFileList.Count == 0)
                        await RestoreHandler.InvokeBulkRestoreStartAsync(modules, result.TaskControl.ProgressToken).ConfigureAwait(false);

                    // Send priority files first (marked with IsPriorityFile=true)
                    foreach (var file in priorityFileList)
                    {
                        var priorityFileRequest = new FileRequest(file.ID, file.OriginalPath, file.TargetPath, file.Hash, file.Length, file.BlocksetID, IsPriorityFile: true, Version: version, BackupTimestamp: backupTimestamp);
                        await self.Output.WriteAsync(priorityFileRequest).ConfigureAwait(false);
                    }

                    // Then send remaining files
                    foreach (var file in remainingFiles)
                        await self.Output.WriteAsync(file.WithVersion(version, backupTimestamp)).ConfigureAwait(false);

                    sw_write_file?.Stop();

                    if (!options.SkipMetadata)
                    {
                        sw_get_folders?.Start();
                        // The enumerables are cast to arrays to force the query to be executed and release the database lock.
                        var folders = await db
                            .GetFolderMetadataToRestoreAsync(result.TaskControl.ProgressToken)
                            .ToArrayAsync()
                            .ConfigureAwait(false);
                        sw_get_folders?.Stop();

                        sw_write_folder?.Start();
                        foreach (var folder in folders)
                            await self.Output.WriteAsync(folder.WithVersion(version, backupTimestamp)).ConfigureAwait(false);
                        sw_write_folder?.Stop();
                    }

                    // Send the alternate data streams last, so their hosts are restored
                    sw_write_file?.Start();
                    foreach (var file in adsStreams)
                        await self.Output.WriteAsync(new FileRequest(file.ID, file.OriginalPath, file.TargetPath, file.Hash, file.Length, file.BlocksetID, IsAlternateDataStream: true, Version: version, BackupTimestamp: backupTimestamp)).ConfigureAwait(false);
                    sw_write_file?.Stop();
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "FileListerError", ex, "Error during file listing");
                    threw_exception = true;
                    throw;
                }
                finally
                {
                    if (!threw_exception)
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "File lister retired");

                    if (options.InternalProfiling)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG, "InternalTimings", $"Get files: {sw_get_files!.ElapsedMilliseconds}ms, Write files: {sw_write_file!.ElapsedMilliseconds}ms, Get folders: {sw_get_folders!.ElapsedMilliseconds}ms, Write folders: {sw_write_folder!.ElapsedMilliseconds}ms");
                    }
                }
            });
        }
    }

}