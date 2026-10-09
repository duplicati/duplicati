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
using System.IO;
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

                // A stop asks the restore to finish what it is doing and stop, so the lister
                // stops handing out work. It is checked per file rather than once up front
                // because every file is written to the channel before any of the later stages
                // run, so a single check would let a large fileset run to completion anyway.
                // Returning is enough to wind the network down: RunTask retires the output
                // channel, which is the same path a completed listing takes.
                bool StopRequested()
                {
                    if (!result.TaskControl.StopToken.IsCancellationRequested)
                        return false;

                    Logging.Log.WriteVerboseMessage(LOGTAG, "StoppedProcess", null, "File lister stopped, no further files will be restored");
                    return true;
                }

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

                    sw_get_folders?.Start();
                    // The enumerables are cast to arrays to force the query to be executed and release the database lock.
                    var folders = options.SkipMetadata
                        ? Array.Empty<FileRequest>()
                        : await db
                            .GetFolderMetadataToRestoreAsync(result.TaskControl.ProgressToken)
                            .ToArrayAsync()
                            .ConfigureAwait(false);
                    sw_get_folders?.Stop();

                    // Separate out the symbolic links to folders that have other entries restored
                    // below them, so they are restored after everything else. A source that is
                    // itself a link has its files restored below the link, and making the link
                    // while they, their folder metadata or their alternate data streams are
                    // written would pull the folder away from under them. Other links stay with
                    // the files, so they are in place before the metadata of their folder is restored.
                    var folderLinks = FindLinksWithRestoredEntriesBelow(files, adsStreams, folders);
                    if (folderLinks.Count > 0)
                        files = files.Where(f => !folderLinks.Contains(f));

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
                        if (StopRequested())
                            return;

                        var priorityFileRequest = new FileRequest(
                            ID: file.ID,
                            OriginalPath: file.OriginalPath,
                            TargetPath: file.TargetPath,
                            Hash: file.Hash,
                            Length: file.Length,
                            BlocksetID: file.BlocksetID,
                            HasRestoredEntriesBelow: false,
                            IsPriorityFile: true,
                            IsAlternateDataStream: false,
                            Version: version,
                            BackupTimestamp: backupTimestamp);
                        await self.Output.WriteAsync(priorityFileRequest).ConfigureAwait(false);
                    }

                    // Then send remaining files
                    foreach (var file in remainingFiles)
                    {
                        if (StopRequested())
                            return;

                        await self.Output.WriteAsync(file.WithVersion(version, backupTimestamp)).ConfigureAwait(false);
                    }

                    sw_write_file?.Stop();

                    sw_write_folder?.Start();
                    foreach (var folder in folders)
                    {
                        if (StopRequested())
                            return;

                        await self.Output.WriteAsync(folder.WithVersion(version, backupTimestamp)).ConfigureAwait(false);
                    }
                    sw_write_folder?.Stop();

                    // Send the alternate data streams after the files, so their hosts are restored
                    sw_write_file?.Start();
                    foreach (var file in adsStreams)
                    {
                        if (StopRequested())
                            return;

                        await self.Output.WriteAsync(new FileRequest(
                            ID: file.ID,
                            OriginalPath: file.OriginalPath,
                            TargetPath: file.TargetPath,
                            Hash: file.Hash,
                            Length: file.Length,
                            BlocksetID: file.BlocksetID,
                            HasRestoredEntriesBelow: false,
                            IsPriorityFile: false,
                            IsAlternateDataStream: true,
                            Version: version,
                            BackupTimestamp: backupTimestamp)).ConfigureAwait(false);
                    }
                    sw_write_file?.Stop();

                    // Send the symbolic links with entries restored below them last, after
                    // everything they hold. The FileProcessor holds them back until everything
                    // else is restored, and makes them one at a time. A link below another is
                    // sent first, so that with a single processor it is made first, into the
                    // folder that holds the restored entries, before the outer link is replaced.
                    sw_write_file?.Start();
                    foreach (var link in folderLinks.OrderByDescending(l => l.TargetPath.Length))
                    {
                        if (StopRequested())
                            return;

                        await self.Output.WriteAsync(new FileRequest(
                            ID: link.ID,
                            OriginalPath: link.OriginalPath,
                            TargetPath: link.TargetPath,
                            Hash: link.Hash,
                            Length: link.Length,
                            BlocksetID: link.BlocksetID,
                            HasRestoredEntriesBelow: true,
                            IsPriorityFile: false,
                            IsAlternateDataStream: false,
                            Version: version,
                            BackupTimestamp: backupTimestamp)).ConfigureAwait(false);
                    }
                    sw_write_file?.Stop();
                }
                catch (Exception) when (RestoreCancellation.IsShutdownRequested(result.TaskControl))
                {
                    // An abort is an orderly shutdown that arrived by a different signal than
                    // retirement, so it is reported the same way retirement is. This is also the
                    // only process without a `catch (RetiredException)`, and consulting the token
                    // covers both.
                    Logging.Log.WriteVerboseMessage(LOGTAG, "CancelledProcess", null, "File lister cancelled");
                    threw_exception = true;
                    throw;
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

        /// <summary>
        /// Finds the symbolic links to folders that have other entries restored below them,
        /// which is the case for a source that is itself a link, as the backup follows it.
        /// </summary>
        /// <param name="files">The files and symbolic links to restore.</param>
        /// <param name="adsStreams">The alternate data streams to restore.</param>
        /// <param name="folders">The folders whose metadata is restored.</param>
        /// <returns>The symbolic links to folders with entries restored below them.</returns>
        private static HashSet<FileRequest> FindLinksWithRestoredEntriesBelow(IEnumerable<FileRequest> files, IEnumerable<FileRequest> adsStreams, IEnumerable<FileRequest> folders)
        {
            var folderLinks = files
                .Where(f => f.BlocksetID == LocalDatabase.SYMLINK_BLOCKSET_ID && f.TargetPath.EndsWith(Path.DirectorySeparatorChar))
                .ToList();
            if (folderLinks.Count == 0)
                return [];

            // With the paths sorted, the entries below a link follow its own path directly,
            // so one lookup per link tells whether there are any.
            var comparison = Library.Utility.Utility.ClientFilenameStringComparison;
            var comparer = StringComparer.FromComparison(comparison);
            var paths = files.Concat(adsStreams).Concat(folders).Select(f => f.TargetPath).ToList();
            paths.Sort(comparer);

            var result = new HashSet<FileRequest>();
            foreach (var link in folderLinks)
            {
                var index = paths.BinarySearch(link.TargetPath, comparer);
                var next = index >= 0 ? index + 1 : ~index;
                if (next < paths.Count && paths[next].StartsWith(link.TargetPath, comparison))
                    result.Add(link);
            }

            return result;
        }
    }

}