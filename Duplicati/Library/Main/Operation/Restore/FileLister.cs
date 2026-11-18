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
using System.Runtime.CompilerServices;
using System.Threading;
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
                    var token = result.TaskControl.ProgressToken;
                    var comparison = Library.Utility.Utility.ClientFilenameStringComparison;
                    var comparer = StringComparer.FromComparison(comparison);

                    // The files are read in batches, so the list of a restore of millions of files is not
                    // held in memory for the whole restore (#7538). The lock on the database is only held
                    // while a batch is read, as before it was only held while the whole list was read.
                    sw_get_files?.Start();
                    await db.PrepareRestoreOrderAsync(token).ConfigureAwait(false);

                    // A first pass collects the entries that are not sent in the order of the list. They
                    // are few: alternate data streams, symbolic links to folders and priority files.
                    // A link is held back only when there are entries to restore below it; that is known
                    // once every candidate is known, so it is decided after this pass.
                    var adsStreams = new List<FileRequest>();
                    var linkCandidates = new List<FileRequest>();
                    var priorityCandidates = new List<FileRequest>();
                    var heldBack = new HashSet<long>();
                    var priorityFileSet = new HashSet<string>(priorityFiles, StringComparer.OrdinalIgnoreCase);
                    await foreach (var file in ReadFilesAsync(db, token).ConfigureAwait(false))
                    {
                        if (SystemIO.IO_OS.SupportsAlternateDataStreams && SystemIO.IO_OS.IsAlternateDataStream(file.TargetPath))
                        {
                            heldBack.Add(file.ID);
                            if (options.DisableAdsRestore)
                                Logging.Log.WriteVerboseMessage(LOGTAG, "SkipAdsRestore", "Skipping ADS restore for {0}", file.TargetPath);
                            else
                                adsStreams.Add(file);
                            continue;
                        }

                        if (file.BlocksetID == LocalDatabase.SYMLINK_BLOCKSET_ID && file.TargetPath.EndsWith(Path.DirectorySeparatorChar))
                            linkCandidates.Add(file);

                        // Resolve which files are priority files. A priority entry only counts
                        // if it matches at least one restorable target; entries a restore callback
                        // module added that match nothing are ignored so the priority-file counter
                        // below stays consistent with the number of priority FileRequests actually
                        // emitted. Without this, a non-matching entry would leave the non-priority
                        // FileProcessors waiting forever for a priority file that never arrives.
                        if (priorityFileSet.Count > 0 && priorityFileSet.Any(pf => file.TargetPath.EndsWith(pf, StringComparison.OrdinalIgnoreCase)))
                            priorityCandidates.Add(file);
                    }

                    // Separate out the symbolic links to folders that have other entries restored
                    // below them, so they are restored after everything else. A source that is
                    // itself a link has its files restored below the link, and making the link
                    // while they, their folder metadata or their alternate data streams are
                    // written would pull the folder away from under them. Other links stay with
                    // the files, so they are in place before the metadata of their folder is restored.
                    var folderLinks = linkCandidates.Count == 0
                        ? []
                        : await FindLinksWithRestoredEntriesBelowAsync(db, linkCandidates, adsStreams, !options.SkipMetadata, comparer, token).ConfigureAwait(false);
                    foreach (var link in folderLinks)
                        heldBack.Add(link.ID);

                    var priorityFileList = priorityCandidates.Where(f => !folderLinks.Contains(f)).ToList();
                    foreach (var file in priorityFileList)
                        heldBack.Add(file.ID);
                    sw_get_files?.Stop();

                    result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_DownloadingRemoteFiles);

                    sw_write_file?.Start();

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
                        await RestoreHandler.InvokeBulkRestoreStartAsync(modules, token).ConfigureAwait(false);

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

                    // Then send remaining files, a batch at a time
                    await foreach (var file in ReadFilesAsync(db, token).ConfigureAwait(false))
                    {
                        if (StopRequested())
                            return;

                        if (!heldBack.Contains(file.ID))
                            await self.Output.WriteAsync(file.WithVersion(version, backupTimestamp)).ConfigureAwait(false);
                    }

                    sw_write_file?.Stop();

                    sw_write_folder?.Start();
                    if (!options.SkipMetadata)
                        await foreach (var folder in ReadFoldersAsync(db, sw_get_folders, token).ConfigureAwait(false))
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
        /// The number of files or folders read from the database at a time.
        /// </summary>
        internal static int BatchSize { get; set; } = 10000;

        /// <summary>
        /// Reads the files and symlinks to restore, in the order of the restore, a batch at a time.
        /// </summary>
        /// <param name="db">The restore database.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>The files and symlinks to restore.</returns>
        private static async IAsyncEnumerable<FileRequest> ReadFilesAsync(LocalRestoreDatabase db, [EnumeratorCancellation] CancellationToken token)
        {
            var afterRank = 0L;
            while (true)
            {
                var batch = await db.GetFilesAndSymlinksToRestoreAsync(afterRank, BatchSize, token).ConfigureAwait(false);
                if (batch.Count == 0)
                    yield break;

                foreach (var (_, file) in batch)
                    yield return file;

                afterRank = batch[^1].Rank;
            }
        }

        /// <summary>
        /// Reads the folders whose metadata is restored, a batch at a time.
        /// </summary>
        /// <param name="db">The restore database.</param>
        /// <param name="sw_get_folders">The stopwatch that times the reads, if profiling.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>The folders to restore.</returns>
        private static async IAsyncEnumerable<FileRequest> ReadFoldersAsync(LocalRestoreDatabase db, Stopwatch? sw_get_folders, [EnumeratorCancellation] CancellationToken token)
        {
            var afterId = 0L;
            while (true)
            {
                sw_get_folders?.Start();
                var batch = await db.GetFolderMetadataToRestoreAsync(afterId, BatchSize, token).ConfigureAwait(false);
                sw_get_folders?.Stop();
                if (batch.Count == 0)
                    yield break;

                foreach (var folder in batch)
                    yield return folder;

                afterId = batch[^1].ID;
            }
        }

        /// <summary>
        /// Finds the symbolic links to folders that have other entries restored below them,
        /// which is the case for a source that is itself a link, as the backup follows it.
        /// </summary>
        /// <param name="db">The restore database, read again for the paths of the files and folders.</param>
        /// <param name="candidates">The symbolic links to folders.</param>
        /// <param name="adsStreams">The alternate data streams to restore.</param>
        /// <param name="includeFolders">If the folders whose metadata is restored are entries to look at.</param>
        /// <param name="comparer">The comparer for paths on this system.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>The symbolic links to folders with entries restored below them.</returns>
        private static async Task<HashSet<FileRequest>> FindLinksWithRestoredEntriesBelowAsync(LocalRestoreDatabase db, List<FileRequest> candidates, List<FileRequest> adsStreams, bool includeFolders, StringComparer comparer, CancellationToken token)
        {
            // The paths of the links end with a separator, so an entry is below a link if the path of
            // one of the folders that hold it is the path of the link
            var byPath = new Dictionary<string, List<FileRequest>>(comparer);
            foreach (var link in candidates)
            {
                if (!byPath.TryGetValue(link.TargetPath, out var list))
                    byPath[link.TargetPath] = list = [];
                list.Add(link);
            }
            var lookup = byPath.GetAlternateLookup<ReadOnlySpan<char>>();
            var result = new HashSet<FileRequest>();

            void Check(FileRequest entry)
            {
                // The last character is left out, so a link does not count as below itself
                var path = entry.TargetPath.AsSpan();
                for (var i = 0; i < path.Length - 1; i++)
                    if (path[i] == Path.DirectorySeparatorChar && lookup.TryGetValue(path.Slice(0, i + 1), out var links))
                        result.UnionWith(links);
            }

            // The same entries as the lists the links were sorted with before: the files and symbolic
            // links, the alternate data streams that are restored, and the folders when their metadata is
            await foreach (var file in ReadFilesAsync(db, token).ConfigureAwait(false))
                if (!(SystemIO.IO_OS.SupportsAlternateDataStreams && SystemIO.IO_OS.IsAlternateDataStream(file.TargetPath)))
                    Check(file);
            foreach (var ads in adsStreams)
                Check(ads);
            if (includeFolders)
                await foreach (var folder in ReadFoldersAsync(db, null, token).ConfigureAwait(false))
                    Check(folder);

            return result;
        }
    }

}