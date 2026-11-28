using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database.Sync;
using Duplicati.Library.Utility;
using Duplicati.Library.Main.Operation.Backup;
using Duplicati.Library.Main.Operation.Common;

#nullable enable

namespace Duplicati.Library.Main.Operation.Sync;

/// <summary>
/// A destination of a sync run. The handler owns none of the members; the caller
/// creates and disposes the backend manager and the database.
/// </summary>
/// <param name="BackendManager">The backend manager of the destination</param>
/// <param name="Database">The sync database (inventory and intent journal) of the destination</param>
/// <param name="DisplayName">The name of the destination for log messages, without credentials</param>
/// <param name="StateMode">How the remote state of the destination is determined</param>
/// <param name="DryRun">Whether nothing is written to or deleted from the destination</param>
internal sealed record SyncTarget(
    IBackendManager BackendManager,
    LocalSyncDatabase Database,
    string DisplayName,
    SyncRemoteState StateMode,
    bool DryRun
);

/// <summary>
/// The counters of one destination of a sync run, bumped per operation as it is
/// planned or performed.
/// </summary>
internal sealed class SyncTargetSummary
{
    /// <summary>The name of the destination, without credentials</summary>
    public required string DisplayName { get; init; }
    public long Upload;
    public long Update;
    public long Delete;
    public long FoldersCreated;
    public long FoldersDeleted;
    public long UnchangedFiles;
    public long Linked;
    public long SizeOfUploadedFiles;
    public long SizeOfDeletedFiles;
    /// <summary>The number of files that could not be uploaded</summary>
    public long UploadFailures;
    /// <summary>The number of files and folders that could not be deleted</summary>
    public long DeleteFailures;
    /// <summary>Set when the destination failed as a whole and was skipped for the rest of the run</summary>
    public bool Failed;
    /// <summary>The message of the failure that set <see cref="Failed"/></summary>
    public string? FailureMessage;

    /// <summary>Whether everything the run planned for the destination was done</summary>
    public bool Succeeded => !Failed && UploadFailures == 0 && DeleteFailures == 0;
}

/// <summary>
/// The outcome of a sync run: the counters of each destination, in the order the
/// destinations were given, and the counters of the single source enumeration.
/// </summary>
/// <param name="Targets">The per-destination counters</param>
/// <param name="SourceFiles">The number of source files seen</param>
/// <param name="SizeOfSourceFiles">The size of the source files seen</param>
internal sealed record SyncRunSummary(
    IReadOnlyList<SyncTargetSummary> Targets,
    long SourceFiles,
    long SizeOfSourceFiles
);

internal class SyncHandler
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<SyncHandler>();
    private readonly Options m_options;
    private readonly IOperationProgressUpdater m_progress;
    private string[] m_sources = [];

    /// <summary>
    /// Creates a sync handler
    /// </summary>
    /// <param name="options">The options of the run</param>
    /// <param name="progress">The progress updater that receives file counts and per-file progress</param>
    public SyncHandler(Options options, IOperationProgressUpdater progress)
    {
        m_options = options;
        m_progress = progress;
    }

    /// <summary>
    /// Copies the run counters into the results of the sync command
    /// </summary>
    /// <param name="results">The results to fill</param>
    /// <param name="summary">The outcome of the run</param>
    public static void ApplyToResults(SyncResults results, SyncRunSummary summary)
    {
        // Upload+Update is the combined "files uploaded" count. The mirror deletes
        // unknown remote files and orphaned folders; snapshot retention removes old
        // snapshot folders.
        results.FoldersCreated = summary.Targets.Sum(x => x.FoldersCreated);
        results.FoldersDeleted = summary.Targets.Sum(x => x.FoldersDeleted);
        results.FilesUploaded = summary.Targets.Sum(x => x.Upload + x.Update);
        results.UnchangedFiles = summary.Targets.Sum(x => x.UnchangedFiles);
        results.FilesDeleted = summary.Targets.Sum(x => x.Delete);
        results.SourceFiles = summary.SourceFiles;
        results.SizeOfSourceFiles = summary.SizeOfSourceFiles;
        results.SizeOfUploadedFiles = summary.Targets.Sum(x => x.SizeOfUploadedFiles);
        results.SizeOfDeletedFiles = summary.Targets.Sum(x => x.SizeOfDeletedFiles);
    }

    /// <summary>
    /// Runs the sync: the source is enumerated once, folder by folder, and what it
    /// holds is applied to every destination.
    /// </summary>
    /// <param name="sourceProvider">The provider of the source entries; owned by the caller</param>
    /// <param name="sources">The source root paths, as the provider knows them</param>
    /// <param name="targets">The destinations; owned by the caller</param>
    /// <param name="filter">The filter that selects the source entries, and the remote files a delete may touch</param>
    /// <param name="maxDepth">The number of folder levels below a source root to descend; zero processes only the files directly in the root, <c>null</c> is unlimited</param>
    /// <param name="reportPhases">Whether the run reports the sync phases; false when the run is a step of another operation</param>
    /// <param name="onTargetStarted">Invoked with the index of the destination and the number of destinations when the work moves to another destination</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The counters of the run</returns>
    public async Task<SyncRunSummary> RunAsync(ISourceProvider sourceProvider, string[] sources, IReadOnlyList<SyncTarget> targets, IFilter filter, int? maxDepth, bool reportPhases, Action<int, int>? onTargetStarted, CancellationToken ct)
    {
        m_sources = sources;
        if (maxDepth < 0)
            throw new UserInformationException($"The maximum depth cannot be negative: {maxDepth}", "InvalidSyncMaxDepth");

        void ReportPhase(OperationPhase phase)
        {
            if (reportPhases)
                m_progress.UpdatePhase(phase);
        }

        ReportPhase(OperationPhase.Sync_Begin);
        Logging.Log.WriteInformationMessage(LOGTAG, "SyncStarted", "Starting sync");

        // Sync mirrors files to the destination unencrypted, so a passphrase is
        // meaningless here and must never be used.
        if (!m_options.NoEncryption)
            throw new UserInformationException("Encryption must be disabled for sync as it is not supported.", "EncryptionNotAllowed");

        var isDelete = m_options.SyncThenDelete;
        var snapshotMode = m_options.SyncSnapshotFolders;
        // The snapshot folder of this run is named after the time the run started, so
        // every destination of the run gets the same name.
        var runStartUtc = DateTime.UtcNow;
        var verifyHash = m_options.SyncVerifyHash;

        // The handler cannot tell whether a destination distinguishes letter case, so
        // it follows the same declaration the backup path does. On a destination that
        // does not (NTFS, APFS), a name that differs only in case IS the remote file:
        // uploading the new casing overwrites it in place, and deleting the old casing
        // afterwards would delete what was just written.
        var nameComparer = m_options.CaseInsensitiveRemote ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        // A snapshot folder starts empty and receives only what the source holds, so
        // there is never anything in it to delete.
        if (snapshotMode && isDelete)
        {
            Logging.Log.WriteInformationMessage(LOGTAG, "SnapshotIgnoresDelete", $"--{Options.SYNC_THEN_DELETE_OPTION} has no effect with --{Options.SYNC_SNAPSHOT_FOLDERS_OPTION}: each snapshot folder holds only the files the source has; use --{Options.SYNC_KEEP_SNAPSHOTS_OPTION} to remove old snapshots.");
            isDelete = false;
        }

        if (snapshotMode && m_options.SyncSnapshotDisableHardlinks)
            Logging.Log.WriteInformationMessage(LOGTAG, "SnapshotHardLinksDisabled", $"--{Options.SYNC_SNAPSHOT_DISABLE_HARDLINKS_OPTION} is set: unchanged files are copied into each snapshot instead of being hard linked.");


        var blacklistPaths = BackupHandler.GetBlacklistedPaths(m_options, sources);
        var fileAttributeFilter = m_options.FileAttributeFilter;
        var symlinkPolicy = m_options.SymlinkPolicy;
        var hardlinkPolicy = m_options.HardlinkPolicy;
        var disableBackupExclusionXattr = m_options.DisableBackupExclusionXattr;
        var excludeEmptyFolders = m_options.ExcludeEmptyFolders;
        var ignoreNames = m_options.IgnoreFilenames;

        // Start a parallel background counter that walks the source tree. With a
        // maximum depth of zero the run reads a single folder, so the count comes
        // from that listing instead.
        using var counterCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? counterTask = null;
        if (!m_options.DisableFileScanner && maxDepth != 0)
        {
            ReportPhase(OperationPhase.Sync_CountingFiles);
            counterTask = CountFilesHandler.RunAsync(
                sourceProvider,
                sources,
                m_progress,
                fileAttributeFilter,
                filter,
                symlinkPolicy,
                hardlinkPolicy,
                disableBackupExclusionXattr,
                ignoreNames,
                blacklistPaths,
                maxDepth,
                counterCts.Token);
        }

        var run = new RunContext(m_progress, onTargetStarted, perTargetProgress: !reportPhases, countFromListing: maxDepth == 0, keepModificationTime: m_options.SyncKeepModificationTime);

        // Per-backend run state: each destination has its own database (its own inventory
        // and intent journal) and its own per-run state-mode resolution (the UseLocalState
        // fallback depends on whether THIS backend's inventory is empty). Computing these
        // once per backend before the shared folder loop keeps the per-folder fan-out cheap
        // and makes a single source enumeration feed every backend, so the same observed
        // local state is replicated to all destinations instead of re-enumerating the
        // source once per backend (where the source could change between enumerations).
        var backendStates = run.States;
        try
        {
            // Collect all initial states from all targets
            foreach (var mgr in targets)
            {
                // --sync-recheck forces a fresh remote listing per folder for this run. Under
                // UseLocalState that also means the local inventory cache is treated as stale:
                // the run effectively behaves as UseRemoteState, and (because we keep the
                // inventory write-through under UseLocalState) the cache is refreshed as we
                // go, so the next UseLocalState run starts from a fresh baseline. Under
                // UseRemoteState and BlindlyUpload the flag is a no-op (UseRemoteState already
                // lists fresh; BlindlyUpload ignores remote state entirely).
                var stateMode = (mgr.StateMode == SyncRemoteState.UseLocalState && m_options.SyncRecheck)
                    ? SyncRemoteState.UseRemoteState
                    : mgr.StateMode;
                var forcePopulateInventory = false;
                var isDryRun = mgr.DryRun;

                // BlindlyUpload has no remote state to diff against, so deleting remote files
                // is not meaningful. Say so once per destination so the user is not surprised
                // that --sync-then-delete is ignored.
                // A run that is a step of another operation chose the mode itself, so
                // there it is only a note.
                if (stateMode == SyncRemoteState.BlindlyUpload && isDelete && !snapshotMode)
                {
                    if (reportPhases)
                        Logging.Log.WriteWarningMessage(LOGTAG, "BlindlyUploadIgnoresDelete", null, $"sync-remote-state={SyncRemoteState.BlindlyUpload} cannot determine which remote files to delete; --{Options.SYNC_THEN_DELETE_OPTION} will be ignored for {{0}} in this run.", mgr.DisplayName);
                    else
                        Logging.Log.WriteInformationMessage(LOGTAG, "BlindlyUploadIgnoresDelete", "Every file is uploaded to {0} without listing it first, so nothing is deleted from it in this run.", mgr.DisplayName);
                }

                // Resume: leftover PendingOperation rows mean a previous run was interrupted
                // mid-operation. Under UseRemoteState each folder is listed fresh this run, so
                // any in-flight intent is naturally superseded by the live remote state and the
                // leftover rows can simply be cleared. Under UseLocalState the inventory may be
                // stale for the interrupted paths; we clear the rows but warn the user that a
                // recheck (UseRemoteState) is the way to reconcile fully. Under BlindlyUpload
                // the journal is never consulted, so leftover rows are just noise; clear them.
                if (await mgr.Database.HasAnyPendingOperationsAsync(ct).ConfigureAwait(false))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "InflightDetected", null, "Detected incomplete operations from a previous run. Clearing the intent journal; {0}.", stateMode == SyncRemoteState.UseLocalState ? $"run with --sync-recheck (or sync-remote-state={SyncRemoteState.UseRemoteState}) to reconcile against a fresh remote listing" : "the current run will re-establish remote state per folder");
                    await mgr.Database.ClearPendingOperationsAsync(ct).ConfigureAwait(false);
                }

                // If UseLocalState is configured but the inventory is empty (e.g. first run, or
                // the database was deleted), there is no baseline to diff against. Fall back to
                // UseRemoteState for this run so each folder is listed fresh; the inventory is
                // populated as we go, so the next UseLocalState run has a baseline. This keeps
                // the first run correct without forcing the user to pass a flag.
                if (stateMode == SyncRemoteState.UseLocalState && !await mgr.Database.HasAnyInventoryAsync(ct).ConfigureAwait(false))
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "InventoryEmpty", "Local sync inventory is empty; listing remote folders fresh for this run to establish a baseline.");
                    stateMode = SyncRemoteState.UseRemoteState;
                    forcePopulateInventory = true;
                }

                // Whether to write the observed state back into the inventory cache as we go.
                // True under UseLocalState (the cache is the baseline and must stay current),
                // under UseRemoteState + verifyHash (so computed hashes are remembered for
                // later hash re-checks), and under the UseLocalState-fallback (so the empty
                // cache gets seeded for the next run).
                SnapshotRunState? snapshot = null;
                if (snapshotMode)
                {
                    snapshot = await PrepareSnapshotAsync(mgr, stateMode, runStartUtc, isDryRun, ct).ConfigureAwait(false);

                    // Under UseLocalState the cache is the baseline, and in snapshot mode the
                    // baseline is the previous snapshot. A cache without rows for it (written
                    // by a mirror run, or for a snapshot that has since been removed) cannot
                    // say what the previous snapshot holds, so list it fresh for this run.
                    if (stateMode == SyncRemoteState.UseLocalState && snapshot.Previous != null
                        && !await mgr.Database.HasAnyInventoryBelowFolderAsync(snapshot.Previous, ct).ConfigureAwait(false))
                    {
                        Logging.Log.WriteInformationMessage(LOGTAG, "SnapshotInventoryMissing", "The local sync inventory does not describe snapshot {0}; listing it fresh for this run.", snapshot.Previous);
                        stateMode = SyncRemoteState.UseRemoteState;
                        forcePopulateInventory = true;
                    }

                    if (stateMode == SyncRemoteState.UseRemoteState && snapshot.Previous != null)
                        await SeedPreviousFoldersAsync(mgr.BackendManager, snapshot, m_sources, ct).ConfigureAwait(false);
                }

                var maintainInventory = stateMode == SyncRemoteState.UseLocalState
                    || (stateMode == SyncRemoteState.UseRemoteState && verifyHash)
                    || forcePopulateInventory;

                backendStates.Add(new BackendRunState(mgr.BackendManager, mgr.Database, stateMode, maintainInventory, snapshot, isDryRun, new SyncTargetSummary { DisplayName = mgr.DisplayName }));
            }

            // Seed the folder queue with one entry per source root. The relative path of a
            // source root follows the relative-path convention used throughout the handler:
            // for a single source the source's contents map directly onto the backend root
            // (relative root ""); for multiple sources each source contributes its own name
            // as a top-level sub-folder on the remote. The local folder entry for each
            // queued root is the source root itself, resolved through the source provider.
            // The queue is shared across all backends: each folder is enumerated ONCE and
            // the observed local children are then replicated to every backend, so the same
            // source state is applied to all destinations from a single enumeration.
            var folderQueue = new Queue<FolderWorkItem>();
            foreach (var src in m_sources)
            {
                var relRoot = GetSourceRelativeRoot(src, m_sources);
                var entry = await GetSourceRootAsync(sourceProvider, src, ct).ConfigureAwait(false);
                if (entry == null || !entry.IsFolder)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SourceRootMissing", null, "Source folder could not be resolved, skipping: {0}", GetSourceWithoutCredentials(src));
                    continue;
                }
                folderQueue.Enqueue(new FolderWorkItem(entry, relRoot, 0));
            }

            // With several sources, each source root is a sub-folder of the destination
            // root, and the folder loop below lists it before it puts files in it. Create
            // the missing ones as the sub-folders of any other folder: process the
            // destination root with the source roots as its only sub-folders, without
            // deleting anything, so files and folders next to them are kept. In snapshot
            // mode the source folders are created with the snapshot folder, and under
            // BlindlyUpload every folder is created before it is written to.
            if (sources.Length > 1)
            {
                var sourceRoots = folderQueue.Select(x => (x.Entry, x.RelativePath)).ToList();
                foreach (var bs in backendStates.Where(x => x.Snapshot == null && x.StateMode != SyncRemoteState.BlindlyUpload))
                {
                    try
                    {
                        await ProcessFolderForBackendAsync(
                            "",
                            new Dictionary<string, (ISourceProviderEntry Entry, long Size, DateTime ModifiedUtc)>(),
                            sourceRoots,
                            bs,
                            run,
                            nameComparer,
                            false,
                            verifyHash,
                            false,
                            _ => true,
                            _ => true,
                            null,
                            ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
                    {
                        bs.Summary.Failed = true;
                        bs.Summary.FailureMessage = ex.Message;
                        Logging.Log.WriteErrorMessage(LOGTAG, "SyncTargetFailed", ex, "Sync to {0} failed and is skipped for the rest of the run: {1}", bs.Summary.DisplayName, ex.Message);
                    }
                }
            }

            ReportPhase(OperationPhase.Sync_ProcessingFiles);
            while (folderQueue.Count > 0)
            {
                if (ct.IsCancellationRequested)
                    break;

                var current = folderQueue.Dequeue();
                await ProcessFolderAsync(
                    current,
                    run,
                    maxDepth,
                    filter,
                    fileAttributeFilter,
                    symlinkPolicy,
                    hardlinkPolicy,
                    disableBackupExclusionXattr,
                    excludeEmptyFolders,
                    ignoreNames,
                    blacklistPaths,
                    nameComparer,
                    isDelete,
                    verifyHash,
                    folderQueue,
                    ct).ConfigureAwait(false);
            }

            // A snapshot becomes the latest one only when the run went through every
            // folder; an interrupted run leaves its folder behind for the next run to remove.
            if (snapshotMode && !ct.IsCancellationRequested)
                foreach (var bs in backendStates.Where(x => !x.Summary.Failed))
                    await FinishSnapshotAsync(bs, m_options.SyncKeepSnapshots, ct).ConfigureAwait(false);
        }
        finally
        {
            // Stop the background counter so it does not outlive the run
            await counterCts.CancelAsync().ConfigureAwait(false);
            if (counterTask != null)
            {
                try
                {
                    await counterTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected: the counter was cancelled to stop the enumeration.
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FileCountFailed", ex, "Background file count failed; progress totals may be incomplete.");
                }
            }
        }

        // The per-folder work is committed; the remaining steps (the plan summary
        // log and the results flush) are bookkeeping rather than uploads, so report
        // the wait-for-upload phase here.
        ReportPhase(OperationPhase.Sync_WaitForUpload);

        foreach (var bs in backendStates)
        {
            var ps = bs.Summary;
            Logging.Log.WriteInformationMessage(LOGTAG, "SyncPlan", "Plan for {0}: Upload {1}, Update {2}, Delete {3}, Link {4}", ps.DisplayName, ps.Upload, ps.Update, ps.Delete, ps.Linked);
            if (ps.UploadFailures > 0 || ps.DeleteFailures > 0)
                Logging.Log.WriteWarningMessage(LOGTAG, "SyncIncomplete", null, "Sync to {0} is incomplete: {1} files could not be uploaded and {2} files could not be deleted.", ps.DisplayName, ps.UploadFailures, ps.DeleteFailures);
        }

        var summary = new SyncRunSummary(backendStates.Select(x => x.Summary).ToList(), run.SourceFiles, run.SizeOfSourceFiles);

        ReportPhase(OperationPhase.Sync_Complete);
        // Final tally: the count is no longer in flux, so mark it done.
        m_progress.UpdatefileCount(summary.SourceFiles, summary.SizeOfSourceFiles, true);

        Logging.Log.WriteInformationMessage(LOGTAG, "SyncFinished", "Sync completed.");
        return summary;
    }

    /// <summary>
    /// Processes a single folder across ALL backends from a single source
    /// enumeration: the folder's local children are enumerated ONCE and then the
    /// observed local state is replicated to every backend. Sub-folders are enqueued
    /// once (the queue is shared across backends) so each folder is enumerated a
    /// single time for the whole run. Only one folder's children are resident in
    /// memory at a time. This guarantees the same source state is applied to every
    /// destination, instead of re-enumerating the source once per backend (where the
    /// source could change between enumerations and the backends would diverge).
    /// </summary>
    private static async Task ProcessFolderAsync(
        FolderWorkItem current,
        RunContext run,
        int? maxDepth,
        IFilter filter,
        FileAttributes fileAttributeFilter,
        Options.SymlinkStrategy symlinkPolicy,
        Options.HardlinkStrategy hardlinkPolicy,
        bool disableBackupExclusionXattr,
        bool excludeEmptyFolders,
        string[]? ignoreNames,
        HashSet<string> blacklistPaths,
        StringComparer nameComparer,
        bool isDelete,
        bool verifyHash,
        Queue<FolderWorkItem> folderQueue,
        CancellationToken ct)
    {
        var folderRelPath = current.RelativePath;
        var folderEntry = current.Entry;
        var atDepthLimit = maxDepth.HasValue && current.Depth >= maxDepth.Value;
        var caseInsensitive = ReferenceEquals(nameComparer, StringComparer.OrdinalIgnoreCase);

        // 1. Enumerate the local folder's direct children (non-recursive) ONCE and
        // split them into sub-folders (to enqueue) and files (to upload this pass).
        // The filter reuses the backup enumeration filter so attribute/symlink/
        // hardlink/blacklist/xattr/ignore-name behavior is identical between backup
        // and sync. Only this folder's children are resident in memory at once, and
        // the same captured children feed every backend so the source is read a single
        // time for the whole run.
        var localSubfolders = new List<(ISourceProviderEntry Entry, string RelativePath)>();
        var localSubfolderNames = new HashSet<string>(nameComparer);
        var localFiles = new Dictionary<string, (ISourceProviderEntry Entry, long Size, DateTime ModifiedUtc)>(nameComparer);

        await foreach (var child in FileEnumerationProcess.EnumerateFolderAsync(
            folderEntry,
            fileAttributeFilter,
            filter,
            symlinkPolicy,
            hardlinkPolicy,
            disableBackupExclusionXattr,
            ignoreNames,
            blacklistPaths,
            ct).ConfigureAwait(false))
        {
            // Build the relative path on the remote destination. For the backend root
            // (folderRelPath == "") the child's name is the relative path; for a nested
            // folder we join the parent's relative path with the child's name using '/'.
            // Validate the name so a ".." or separator-bearing name (possible on exotic
            // source providers/filesystems) cannot escape the synced destination tree
            // when it is later uploaded or used to compute a remote path.
            var childName = GetEntryName(child.Path);
            if (!TryBuildSafeRelativePath(folderRelPath, childName, out var childRelPath))
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "InvalidLocalName", null, "Skipping local source entry with an unsafe name: {0}", child.Path);
                continue;
            }

            if (child.IsFolder)
            {
                // On a case-insensitive destination two sub-folders that differ only in
                // case are one remote folder; processing both would make the second pass
                // delete what the first uploaded. Keep the first and say so.
                if (!localSubfolderNames.Add(childName))
                {
                    if (caseInsensitive)
                        Logging.Log.WriteWarningMessage(LOGTAG, "CaseCollision", null, "The source has more than one folder whose names differ only in case, and the destination does not distinguish case; only the first one is synchronized, skipping: {0}", child.Path);
                    continue;
                }
                localSubfolders.Add((child, childRelPath));
            }
            else
            {
                // Count every local source file encountered (regardless of whether it
                // ends up uploaded, unchanged, or de-duplicated) so SourceFiles and
                // SizeOfSourceFiles reflect the full local inventory scanned this run.
                // The source is enumerated once for the whole run, so the count is the
                // true source total regardless of how many backends receive it.
                run.SourceFiles++;
                run.SizeOfSourceFiles += Math.Max(child.Size, 0);

                // INSERT OR IGNORE semantics: the first entry seen for a path wins, so a
                // later duplicate in the same folder is ignored (paths are unique within
                // a single folder in practice). With a case-insensitive destination the
                // duplicate is a real file that cannot be mirrored, so it is reported.
                if (!localFiles.ContainsKey(childRelPath))
                    localFiles[childRelPath] = (child, child.Size, child.LastModificationUtc);
                else if (caseInsensitive)
                    Logging.Log.WriteWarningMessage(LOGTAG, "CaseCollision", null, "The source has more than one file whose names differ only in case, and the destination does not distinguish case; only the first one is synchronized, skipping: {0}", child.Path);
            }
        }

        // 2. Enqueue sub-folders ONCE for the shared queue, after the (single)
        // empty-folder peek. Each backend then independently ensures a sub-folder
        // exists based on its OWN remote state in the per-backend fan-out below, but
        // the sub-folder is processed (enumerated) only once for the whole run.
        //
        // ExcludeEmptyFolders: a sub-folder with no local children must not be
        // created on the remote, matching the backup behavior of not creating empty
        // folders at all. We peek the sub-folder's direct children FIRST and, if it
        // is empty, skip it entirely (neither ensured nor queued) - this is one
        // extra enumeration per sub-folder but keeps memory bounded and ensures an
        // empty folder never triggers a CreateFolder call. The peek only runs when
        // the option is set; without it every sub-folder is ensured/queued as before.
        //
        // Maximum depth: the sub-folders of a folder at the depth limit are dropped
        // before the peek and before they are enqueued, so they are neither read
        // nor created on any destination.
        if (atDepthLimit)
        {
            foreach (var (_, subRelPath) in localSubfolders)
                Logging.Log.WriteVerboseMessage(LOGTAG, "SkippingFolderBeyondDepth", "Skipping folder beyond the maximum depth: {0}", subRelPath);
            localSubfolders.Clear();
        }

        // With a depth of zero there is no background counter; this listing is the count.
        if (run.CountFromListing)
            run.Progress.UpdatefileCount(run.SourceFiles, run.SizeOfSourceFiles, true);

        var enqueuedSubfolders = new List<(ISourceProviderEntry Entry, string RelativePath)>();
        foreach (var (subEntry, subRelPath) in localSubfolders)
        {
            if (excludeEmptyFolders)
            {
                bool hasAny = false;
                await foreach (var _ in FileEnumerationProcess.EnumerateFolderAsync(
                    subEntry, fileAttributeFilter, filter, symlinkPolicy, hardlinkPolicy,
                    disableBackupExclusionXattr, ignoreNames, blacklistPaths, ct).ConfigureAwait(false))
                {
                    hasAny = true;
                    break;
                }
                if (!hasAny)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "SkippingEmptyFolder", "Skipping empty folder: {0}", subRelPath);
                    continue;
                }
            }

            enqueuedSubfolders.Add((subEntry, subRelPath));
            folderQueue.Enqueue(new FolderWorkItem(subEntry, subRelPath, current.Depth + 1));
        }

        // 3. Replicate the captured local state to each backend. For every backend we
        // resolve that backend's remote state for this folder, ensure any missing
        // sub-folders, upload/update files, and (optionally) delete unknown remote
        // files. The local children (localFiles / enqueuedSubfolders) are shared, so
        // each backend applies the SAME observed source state; the source is not
        // re-enumerated per backend, which keeps the destinations mutually consistent
        // even if the source were to change mid-run.
        //
        // A remote file takes part in a delete only when the filter would have let a
        // source file of that name through: what the filter hides on the source side
        // is not this run's to remove on the destination side either. The same goes
        // for a remote folder, which is matched as the enumeration matches a source
        // folder: with only includes in the filter every folder is let through, so an
        // include can match a file below it. A remote folder that is deleted is deleted
        // only as far as the filter reaches: what the filter excludes below it is kept.
        var folderPath = Library.Common.IO.Util.AppendDirSeparator(folderEntry.Path);
        var folderFilter = FileEnumerationProcess.GetEnumerateFilter(filter);
        bool PassesFilter(string name) => FilterExpression.Matches(filter, folderPath + name, out _);
        bool PassesFolderFilter(string name) => FilterExpression.Matches(folderFilter, Library.Common.IO.Util.AppendDirSeparator(folderPath + name), out _);
        bool InFilterScope(string relativePath, bool isFolder)
        {
            var path = folderPath + relativePath.Replace("/", Library.Common.IO.Util.DirectorySeparatorString);
            return isFolder
                ? FilterExpression.Matches(folderFilter, Library.Common.IO.Util.AppendDirSeparator(path), out _)
                : FilterExpression.Matches(filter, path, out _);
        }
        Func<string, bool, bool>? orphanTreeScope = filter == null || filter.Empty ? null : InFilterScope;

        for (var i = 0; i < run.States.Count; i++)
        {
            var bs = run.States[i];
            // A destination that failed is skipped for the rest of the run, so one
            // unreachable destination does not stop the others.
            if (bs.Summary.Failed)
                continue;

            run.EnterTarget(i);
            try
            {
                if (bs.Snapshot != null)
                    await ProcessSnapshotFolderForBackendAsync(
                        folderRelPath,
                        localFiles,
                        enqueuedSubfolders,
                        bs,
                        bs.Snapshot,
                        run,
                        nameComparer,
                        verifyHash,
                        ct).ConfigureAwait(false);
                else
                    await ProcessFolderForBackendAsync(
                        folderRelPath,
                        localFiles,
                        enqueuedSubfolders,
                        bs,
                        run,
                        nameComparer,
                        isDelete,
                        verifyHash,
                        atDepthLimit,
                        PassesFilter,
                        PassesFolderFilter,
                        orphanTreeScope,
                        ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
            {
                bs.Summary.Failed = true;
                bs.Summary.FailureMessage = ex.Message;
                Logging.Log.WriteErrorMessage(LOGTAG, "SyncTargetFailed", ex, "Sync to {0} failed and is skipped for the rest of the run: {1}", bs.Summary.DisplayName, ex.Message);
            }
        }
    }

    /// <summary>
    /// Applies the already-enumerated local state of a single folder to one backend:
    /// resolves the backend's remote state for the folder, ensures any missing
    /// sub-folders, uploads/updates the shared local files, and (optionally) deletes
    /// unknown remote files. The local children are supplied by the caller (captured
    /// once for the whole folder) so this method performs no source enumeration; it
    /// only touches the backend and this backend's own database.
    /// </summary>
    private static async Task ProcessFolderForBackendAsync(
        string folderRelPath,
        Dictionary<string, (ISourceProviderEntry Entry, long Size, DateTime ModifiedUtc)> localFiles,
        List<(ISourceProviderEntry Entry, string RelativePath)> enqueuedSubfolders,
        BackendRunState bs,
        RunContext run,
        StringComparer nameComparer,
        bool isDelete,
        bool verifyHash,
        bool atDepthLimit,
        Func<string, bool> passesFilter,
        Func<string, bool> passesFolderFilter,
        Func<string, bool, bool>? orphanTreeScope,
        CancellationToken ct)
    {
        var backendManager = bs.BackendManager;
        var db = bs.Database;
        var stateMode = bs.StateMode;
        var maintainInventory = bs.MaintainInventory;
        var isDryRun = bs.DryRun;
        var planSummary = bs.Summary;

        // Check remote state for the current folder. Under UseRemoteState we list
        // the remote folder; under UseLocalState we read the inventory cache; under
        // BlindlyUpload we have no remote state. The remote state is keyed by the
        // child's name (not relative path) for easy lookup against the local files,
        // and a parallel set of remote sub-folder names drives the
        // "create missing folders" step. The comparer is the one the destination is
        // declared to use (see nameComparer in RunAsync).
        var remoteFileState = new Dictionary<string, RemoteChild>(nameComparer);
        var remoteFolderNames = new HashSet<string>(nameComparer);

        if (stateMode == SyncRemoteState.UseRemoteState)
        {
            var remoteEntries = (await backendManager.ListAsync(string.IsNullOrEmpty(folderRelPath) ? null : folderRelPath, ct).ConfigureAwait(false)).ToList();

            // A listing that reports a name twice cannot be mirrored: one of the two
            // would silently replace the other in the comparison below.
            Library.Utility.Utility.GetUniqueItems(remoteEntries.Where(x => !x.IsFolder).Select(x => x.Name ?? ""), nameComparer, out var duplicateNames);
            if (duplicateNames.Count > 0)
            {
                var message = string.Format("Found remote files reported as duplicates in the destination {0}, either the backend module is broken or you need to manually remove the extra copies.\nThe following files were found multiple times: {1}", planSummary.DisplayName, string.Join(", ", duplicateNames));
                Logging.Log.WriteErrorMessage(LOGTAG, "DuplicateRemoteFiles", null, message);
                throw new RemoteListVerificationException(message, "DuplicateRemoteFiles");
            }

            foreach (var re in remoteEntries)
            {
                // Several backends (e.g. S3, Google Drive, Box, OneDrive, Dropbox, SMB)
                // name folder entries with a trailing slash by convention. Strip it so
                // the name validates and matches the local sub-folder name; without this
                // every remote folder would be rejected below and re-created on each run.
                var name = re.Name;
                if (re.IsFolder && name != null && name.Length > 1 && name[^1] == '/')
                    name = name[..^1];

                // Validate the backend-supplied name before using it. A malicious or
                // compromised backend could return a name containing "..", path
                // separators, or a backslash; such a name would otherwise flow into
                // delete/upload/list relative paths and escape the synced destination
                // tree on folder-enabled backends (e.g. the File backend joins the
                // relative path onto its root). Skip the entry with a warning rather
                // than carrying it through.
                if (string.IsNullOrWhiteSpace(name) || !IsValidEntryName(name))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "InvalidRemoteName", null, "Skipping remote entry with an unsafe name (contains path separators or parent-directory segments): {0}", name);
                    continue;
                }

                if (re.IsFolder)
                {
                    remoteFolderNames.Add(name);
                    continue;
                }

                // Size is clamped to >= 0 because some backends return -1 to mean
                // "unknown size"; the inventory stores a non-negative size and the
                // diff treats -1 as "do not cross-check size". The last-modification
                // time is normalized to UTC because IFileEntry.LastModification may be
                // Local/Unspecified-kind and the local entry time is UTC; comparing
                // DateTimes of mixed Kind compares raw ticks without conversion, which
                // would treat a local noon as later than a UTC morning and skip updates.
                remoteFileState[name] = new RemoteChild(name, Math.Max(re.Size, 0), AsUtc(re.LastModification), null);
            }

            // If we are maintaining the inventory (write-through under UseLocalState,
            // or hash-remembering under UseRemoteState + verifyHash), reconcile the
            // freshly observed listing with the cache. The listing carries no content
            // hash, so for files already in the inventory we keep the stored hash and
            // update only size/mtime; for files new to the cache we insert with a null
            // hash (the hash will be filled in when the file is uploaded). When
            // verifyHash is on we also enrich the per-file remote state with the stored
            // hash so the upload-decision step can do a hash re-check. Sub-folders are
            // not tracked in the inventory (it only holds file rows); folder existence
            // is derived from file presence.
            if (maintainInventory)
            {
                // Build a name->inventory-row lookup for this folder once, so the
                // per-file enrichment and upsert below share the same view.
                var invByName = new Dictionary<string, LocalSyncDatabase.InventoryItem>(nameComparer);
                await foreach (var inv in db.GetInventoryItemsInFolderAsync(folderRelPath, ct).ConfigureAwait(false))
                {
                    var invName = GetEntryName(inv.RelativePath);
                    if (!string.IsNullOrEmpty(invName))
                        invByName[invName] = inv;
                }

                foreach (var rc in remoteFileState.Values)
                {
                    var relPath = string.IsNullOrEmpty(folderRelPath) ? rc.Name : folderRelPath + "/" + rc.Name;
                    string? storedHash = null;
                    if (invByName.TryGetValue(rc.Name, out var existing))
                    {
                        storedHash = existing.ContentHash;
                        // Preserve the stored hash: the listing has none, so upsert with
                        // the existing hash to avoid wiping it out.
                        await db.UpsertInventoryAsync(relPath, rc.Size, rc.LastModified, storedHash, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // New remote file not yet in the cache: insert with a null hash.
                        await db.UpsertInventoryAsync(relPath, rc.Size, rc.LastModified, null, ct).ConfigureAwait(false);
                    }

                    // Enrich the per-file remote state used by the upload-decision step
                    // so a hash re-check is possible when verifyHash is on.
                    if (verifyHash && !string.IsNullOrEmpty(storedHash))
                        remoteFileState[rc.Name] = rc with { ContentHash = storedHash };
                }
            }
        }
        else if (stateMode == SyncRemoteState.UseLocalState)
        {
            await foreach (var inv in db.GetInventoryItemsInFolderAsync(folderRelPath, ct).ConfigureAwait(false))
            {
                // The inventory only stores file rows (folders are implicit), so every
                // row here is a file child of this folder. Derive the child name from
                // the relative path.
                var name = GetEntryName(inv.RelativePath);
                if (string.IsNullOrEmpty(name))
                    continue;
                remoteFileState[name] = new RemoteChild(name, inv.Size, inv.LastModified, inv.ContentHash);
            }
        }
        // BlindlyUpload: no remote state. remoteFileState and remoteFolderNames stay empty.

        // The sub-folders the destination has and the source does not are deleted
        // after the files, further down. They are worked out here, before anything is
        // written, so a destination that must not be mirrored onto is left as it was.
        // At the depth limit the sub-folders were never read, so nothing is known
        // about them and none of them is an orphan.
        List<string> orphanedFolders = [];
        if (isDelete && !atDepthLimit && stateMode != SyncRemoteState.BlindlyUpload)
        {
            orphanedFolders = await GetOrphanedFoldersAsync(folderRelPath, enqueuedSubfolders, remoteFolderNames, bs, nameComparer, passesFolderFilter, ct).ConfigureAwait(false);

            // The snapshot folders of a destination written with --sync-snapshot-folders
            // are in its root, and to a mirror they are folders the source does not have.
            if (string.IsNullOrEmpty(folderRelPath))
            {
                var snapshots = orphanedFolders.Where(x => SyncSnapshotLayout.TryParseFolderName(x, out _)).Order(StringComparer.Ordinal).ToList();
                if (snapshots.Count > 0)
                    throw new UserInformationException($"The destination {planSummary.DisplayName} holds snapshot folders written with --{Options.SYNC_SNAPSHOT_FOLDERS_OPTION} ({string.Join(", ", snapshots)}), and --{Options.SYNC_THEN_DELETE_OPTION} would delete them because the source has no folders with these names. Use --{Options.SYNC_SNAPSHOT_FOLDERS_OPTION} to keep writing snapshots, or remove the snapshot folders from the destination before mirroring to it.", "SyncDestinationHasSnapshots");
            }
        }

        // Create any sub-folders that are missing on THIS backend. Under UseRemoteState
        // we know exactly which sub-folders already exist (from the listing); under
        // UseLocalState we treat a sub-folder as existing if the inventory has any file
        // under it, otherwise we ensure it; under BlindlyUpload we ensure every
        // sub-folder unconditionally (the root is also ensured below). Ensuring a
        // folder that already exists is a no-op on the backend (CreateFolder treats
        // FolderAlreadyExisted as success) but still a network call, so we avoid it
        // when we have positive evidence the folder exists. The sub-folder set is the
        // shared, once-enumerated set; only the existence check is per-backend.
        foreach (var (_, subRelPath) in enqueuedSubfolders)
        {
            var subName = GetEntryName(subRelPath);
            var existsRemotely = stateMode switch
            {
                SyncRemoteState.UseRemoteState => remoteFolderNames.Contains(subName),
                SyncRemoteState.UseLocalState => await FolderHasInventoryAsync(db, subRelPath, ct).ConfigureAwait(false),
                SyncRemoteState.BlindlyUpload => false, // always ensure under blind upload
                _ => false
            };

            if (!existsRemotely)
            {
                if (isDryRun)
                    Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would create folder: {0}", subRelPath);
                else
                {
                    await backendManager.EnsureFolderAsync(subRelPath, ct).ConfigureAwait(false);
                }
                planSummary.FoldersCreated++;
            }
        }

        // Under BlindlyUpload the current folder must also be ensured to exist before
        // we put files into it (we have no listing to confirm it). For UseRemoteState
        // and UseLocalState the folder's existence is implied: under UseRemoteState we
        // just listed it (so it exists); under UseLocalState we assume the inventory
        // is authoritative, and a folder containing files we're about to upload-into
        // either already has files (so it exists) or is brand new, in which case the
        // sub-folder ensure above created it when it was enqueued as a child. The
        // backend root (folderRelPath == "") is assumed to exist for all modes.
        // BlindlyUpload always treats the current folder as new (we have no listing
        // to prove it exists), so it counts as a created folder when not a dry run.
        if (stateMode == SyncRemoteState.BlindlyUpload && !string.IsNullOrEmpty(folderRelPath))
        {
            if (!isDryRun)
                await backendManager.EnsureFolderAsync(folderRelPath, ct).ConfigureAwait(false);
            planSummary.FoldersCreated++;
        }

        // Upload all new or changed files in this folder to THIS backend. The caller
        // guarantees the current folder exists, so UploadOrUpdateAsync does not call
        // EnsureFolderAsync. The local file entries are shared across backends; each
        // backend opens its own read stream from the same entry (OpenRead is
        // repeatable) and writes its own temp file / hash, so backends do not share
        // mutable upload state.
        foreach (var (relPath, (entry, size, modifiedUtc)) in localFiles)
        {
            ct.ThrowIfCancellationRequested();

            var name = GetEntryName(relPath);
            var remoteFound = remoteFileState.TryGetValue(name, out var remoteEntry);

            // Decide whether an upload is needed. For BlindlyUpload we always upload.
            // For UseRemoteState/UseLocalState we upload when the file is absent or
            // when size/mtime differ; under verifyHash we also re-check the hash when
            // size+mtime are unchanged but a remote hash exists.
            var needsUpload = stateMode == SyncRemoteState.BlindlyUpload;
            var hashRecheck = false;
            string? remoteHash = null;

            if (!needsUpload)
            {
                if (!remoteFound || remoteEntry == null)
                {
                    needsUpload = true;
                }
                else
                {
                    remoteHash = remoteEntry.ContentHash;
                    // A destination that keeps the local time may store it in whole seconds
                    // (WebDAV), which would otherwise look older than the local file on every run
                    var localModifiedUtc = run.KeepModificationTime
                        ? Library.Utility.Utility.NormalizeDateTime(modifiedUtc)
                        : modifiedUtc;
                    if (remoteEntry.Size != size || remoteEntry.LastModified < localModifiedUtc)
                    {
                        needsUpload = true;
                    }
                    else if (verifyHash && !string.IsNullOrEmpty(remoteEntry.ContentHash)
                             && remoteEntry.Size == size && remoteEntry.LastModified >= localModifiedUtc)
                    {
                        // Size and mtime are unchanged but a remote hash exists: re-check
                        // the local hash and skip the upload if it still matches.
                        hashRecheck = true;
                        needsUpload = true;
                    }
                }
            }

            if (!needsUpload)
            {
                // Verbose trace of the skip decision.
                Logging.Log.WriteVerboseMessage(LOGTAG, "SyncSkipUnchanged", "Skipping unchanged file: {0} (local size={1}, mtime={2:O}; remote size={3}, mtime={4:O})", relPath, size, modifiedUtc, remoteEntry?.Size, remoteEntry?.LastModified);
                planSummary.UnchangedFiles++;
                // Count an unchanged file as processed too, so the processed-files
                // tally advances even when no upload is performed.
                run.ReportProcessed(bs);
                continue;
            }

            if (hashRecheck)
            {
                try
                {
                    using (var s = await entry.OpenRead(ct).ConfigureAwait(false))
                    using (var hasher = HashFactory.CreateHasher("SHA256"))
                    {
                        var hash = Convert.ToBase64String(hasher.ComputeHash(s));
                        if (hash == remoteHash)
                        {
                            planSummary.UnchangedFiles++;
                            run.ReportProcessed(bs);
                            continue; // Content unchanged; no upload needed.
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FailedToReadFileHash", ex, "Failed to read local file hash for: {0}", relPath);
                }
            }

            var operation = remoteFound ? SyncOperation.Update : SyncOperation.Upload;
            var label = remoteFound ? "Updating" : "Uploading";

            // On a case-insensitive destination a remote file whose name differs only in
            // case from the local one is the file being updated, so the update is
            // addressed by the destination's own name. This keeps the log, the intent
            // journal and the inventory on the one name the destination reports, rather
            // than adding a second inventory row that differs only in case.
            var targetRelPath = relPath;
            if (remoteFound && remoteEntry != null && !string.Equals(remoteEntry.Name, name, StringComparison.Ordinal)
                && TryBuildSafeRelativePath(folderRelPath, remoteEntry.Name, out var remoteRelPath))
                targetRelPath = remoteRelPath;

            try
            {
                await UploadOrUpdateAsync(bs, run, targetRelPath, entry, operation, label, verifyHash, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
            {
                planSummary.UploadFailures++;
                Logging.Log.WriteWarningMessage(LOGTAG, "UploadFailed", ex, "Failed to {0} file: {1}", label, relPath);
            }
        }

        // (Optionally) delete unknown remote files in this folder on THIS backend. A
        // remote file is "unknown" if no local file with the same name exists in this
        // folder. Under BlindlyUpload we have no remote state, so deletes are skipped
        // (the warning was logged at the start of the run). Pending-operation intent
        // rows are not relevant here since the journal was cleared at the start of
        // the run; a delete cannot race its own upload because the upload for this
        // folder completed above before we delete.
        if (isDelete && stateMode != SyncRemoteState.BlindlyUpload)
        {
            // Collect the paths of files actually deleted from the remote so their
            // inventory removal + intent clearance can be committed in a single
            // transaction at the end of the folder. Batching the per-file DB mutations
            // avoids one BEGIN/COMMIT (one fsync) per deleted file; the intent record
            // for each file is still written BEFORE its backend call so a crash
            // mid-delete is recoverable (the leftover rows are reconciled on the next
            // run). If a crash happens after some deletes succeed but before the batched
            // commit, those rows remain in the inventory/intent journal and are
            // reconciled against a fresh listing on resume.
            var deletedPaths = new List<string>();
            foreach (var (name, rc) in remoteFileState)
            {
                // Map the remote child name back to a relative path. A remote file is
                // unknown if no local file exists at the corresponding relative path.
                // Names in remoteFileState were validated at ingestion, but
                // TryBuildSafeRelativePath guards defensively against ".." or separator
                // segments so the delete can never escape the synced destination tree.
                if (!TryBuildSafeRelativePath(folderRelPath, name, out var relPath))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "InvalidRemoteName", null, "Skipping delete of remote entry with an unsafe name: {0}", name);
                    continue;
                }
                if (localFiles.ContainsKey(relPath))
                    continue;

                // The filter scopes the delete as it scopes the upload
                if (!passesFilter(name))
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "SkippingDeleteByFilter", "Not deleting remote file excluded by the filter: {0}", relPath);
                    continue;
                }

                if (isDryRun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would delete: {0}", relPath);
                    planSummary.Delete++;
                    planSummary.SizeOfDeletedFiles += Math.Max(rc.Size, 0);
                    continue;
                }

                Logging.Log.WriteInformationMessage(LOGTAG, "Deleting", "Deleting {0}", relPath);
                // Record delete intent BEFORE the backend call so a crash mid-delete is
                // recoverable; the row is cleared (in the batched commit below, or on the
                // next run if this one crashes before the commit) at the start of the
                // next run if leftover.
                await db.UpsertPendingOperationAsync(relPath, SyncOperation.Delete, null, null, ct).ConfigureAwait(false);
                try
                {
                    await backendManager.DeleteWithPathAsync(relPath, 0, true, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
                {
                    // One file that cannot be deleted does not stop the run; the intent
                    // row stays and is reconciled on the next run.
                    planSummary.DeleteFailures++;
                    Logging.Log.WriteWarningMessage(LOGTAG, "DeleteFailed", ex, "Failed to delete file: {0}", relPath);
                    continue;
                }
                deletedPaths.Add(relPath);
                planSummary.Delete++;
                planSummary.SizeOfDeletedFiles += Math.Max(rc.Size, 0);
            }

            // Advance the processed-files counter by the files deleted in this folder
            // so the UI progress reflects deletes too. Deletes do not contribute to
            // the uploaded size, so only the count side moves relative to uploads.
            if (planSummary.Delete > 0)
                run.ReportProcessed(bs, includeDeletes: true);

            // Commit the inventory removals and intent clearances for all files deleted
            // in this folder in one transaction, so only one fsync covers the folder.
            // Under UseRemoteState (not maintaining inventory) the inventory removal is
            // a no-op on an empty table but keeps the code path uniform.
            if (deletedPaths.Count > 0)
            {
                await db.ExecuteInTransactionAsync(async txCt =>
                {
                    foreach (var relPath in deletedPaths)
                    {
                        await db.RemoveInventoryAsync(relPath, txCt).ConfigureAwait(false);
                        await db.RemovePendingOperationAsync(relPath, txCt).ConfigureAwait(false);
                    }
                }, ct).ConfigureAwait(false);
            }

            // The files in a folder that is deleted file by file count as deleted files
            var deletedBeforeFolders = planSummary.Delete;
            await DeleteOrphanedFoldersAsync(folderRelPath, orphanedFolders, bs, orphanTreeScope, ct).ConfigureAwait(false);
            if (planSummary.Delete != deletedBeforeFolders)
                run.ReportProcessed(bs, includeDeletes: true);
        }
    }

    /// <summary>
    /// Finds the sub-folders of a folder that exist on the destination but no longer in
    /// the source. The folder loop only visits folders the source has, so a folder
    /// removed from the source would otherwise stay on the destination with all its
    /// files. A folder the filter keeps out of the source is not an orphan: what the
    /// filter hides on the source side is not this run's to remove.
    /// </summary>
    /// <param name="folderRelPath">The relative path of the folder being processed</param>
    /// <param name="enqueuedSubfolders">The sub-folders the source has and that are synchronized</param>
    /// <param name="remoteFolderNames">The sub-folder names a listing showed, under <see cref="SyncRemoteState.UseRemoteState"/></param>
    /// <param name="bs">The state of the destination</param>
    /// <param name="nameComparer">The comparer for names on the destination</param>
    /// <param name="passesFolderFilter">Whether the filter lets a source sub-folder of the given name through</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The relative paths of the orphaned sub-folders</returns>
    private static async Task<List<string>> GetOrphanedFoldersAsync(
        string folderRelPath,
        List<(ISourceProviderEntry Entry, string RelativePath)> enqueuedSubfolders,
        HashSet<string> remoteFolderNames,
        BackendRunState bs,
        StringComparer nameComparer,
        Func<string, bool> passesFolderFilter,
        CancellationToken ct)
    {
        // Under UseRemoteState the listing shows the sub-folders; under UseLocalState the
        // cache knows a sub-folder when it holds a file below it.
        IEnumerable<string> remoteSubfolders = bs.StateMode switch
        {
            SyncRemoteState.UseRemoteState => remoteFolderNames,
            SyncRemoteState.UseLocalState => await bs.Database.GetInventorySubfolderNamesAsync(folderRelPath, ct).ConfigureAwait(false),
            _ => []
        };

        var orphans = new List<string>();
        var localSubfolderNames = new HashSet<string>(enqueuedSubfolders.Select(x => GetEntryName(x.RelativePath)), nameComparer);
        foreach (var name in remoteSubfolders.Distinct(nameComparer))
        {
            if (localSubfolderNames.Contains(name))
                continue;

            if (!IsValidEntryName(name) || !TryBuildSafeRelativePath(folderRelPath, name, out var relPath))
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "InvalidRemoteName", null, "Skipping delete of remote folder with an unsafe name: {0}", name);
                continue;
            }

            // The filter scopes the delete as it scopes the upload
            if (!passesFolderFilter(name))
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "SkippingDeleteByFilter", "Not deleting remote folder excluded by the filter: {0}", relPath);
                continue;
            }

            orphans.Add(relPath);
        }

        return orphans;
    }

    /// <summary>
    /// Deletes the orphaned sub-folders of a folder, with everything in them
    /// (<see cref="GetOrphanedFoldersAsync"/>).
    /// </summary>
    /// <param name="folderRelPath">The relative path of the folder holding the orphaned sub-folders</param>
    /// <param name="orphanedFolders">The relative paths of the folders to delete</param>
    /// <param name="bs">The state of the destination</param>
    /// <param name="treeScope">Whether an entry may be deleted, given its path relative to <paramref name="folderRelPath"/> ('/'-separated) and whether it is a folder; null when the filter is empty and everything may be deleted</param>
    /// <param name="ct">The cancellation token</param>
    private static async Task DeleteOrphanedFoldersAsync(string folderRelPath, List<string> orphanedFolders, BackendRunState bs, Func<string, bool, bool>? treeScope, CancellationToken ct)
    {
        var db = bs.Database;
        foreach (var relPath in orphanedFolders)
        {
            var name = GetEntryName(relPath);
            Func<string, bool, bool>? scope = treeScope == null
                ? null
                : (rel, isFolder) => treeScope(name + "/" + rel, isFolder);

            // Record the intent before the backend call, as for a file delete, so an
            // interrupted delete is reported on the next run.
            if (!bs.DryRun)
                await db.UpsertPendingOperationAsync(relPath, SyncOperation.Delete, null, null, ct).ConfigureAwait(false);

            DeleteTreeResult result;
            try
            {
                result = await DeleteFolderTreeAsync(bs, relPath, "folder", "DeletingFolder", (mgr, path, dryRun, c) => SyncRemoteFolders.DeleteTreeAsync(mgr, path, scope, dryRun, c), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
            {
                // One folder that cannot be deleted does not stop the run; the intent
                // row stays and is reconciled on the next run.
                bs.Summary.DeleteFailures++;
                Logging.Log.WriteWarningMessage(LOGTAG, "DeleteFolderFailed", ex, "Failed to delete folder: {0}", relPath);
                continue;
            }

            if (bs.DryRun)
                continue;

            await db.ExecuteInTransactionAsync(async txCt =>
            {
                // A file that is still there, because it could not be deleted or the filter
                // excludes it, keeps its row, so the cache keeps knowing the folder; only the
                // deleted files lose theirs
                if (result.FilesFailed > 0 || result.EntriesKept > 0)
                    foreach (var deleted in result.DeletedFiles)
                        await db.RemoveInventoryAsync(deleted, txCt).ConfigureAwait(false);
                else
                    await db.RemoveInventoryBelowFolderAsync(relPath, txCt).ConfigureAwait(false);

                // A failed file leaves the intent row, so the next run reports it
                if (result.FilesFailed == 0)
                    await db.RemovePendingOperationAsync(relPath, txCt).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes a folder tree from a destination, or in a dry run works out what would be
    /// deleted, and logs and counts the outcome. A backend that can delete a folder tree
    /// does so in one call. Otherwise the files are deleted one by one and the emptied
    /// folders stay; a later run finds them again with nothing in them, which is neither
    /// logged nor counted as a delete.
    /// </summary>
    /// <param name="bs">The state of the destination</param>
    /// <param name="relPath">The relative path of the folder</param>
    /// <param name="description">What the folder is, for the log messages</param>
    /// <param name="logId">The id of the log message that reports the delete</param>
    /// <param name="deleteTree">The method that deletes the tree</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>What was deleted, or in a dry run would be</returns>
    private static async Task<DeleteTreeResult> DeleteFolderTreeAsync(BackendRunState bs, string relPath, string description, string logId, Func<IBackendManager, string, bool, CancellationToken, Task<DeleteTreeResult>> deleteTree, CancellationToken ct)
    {
        var isDryRun = bs.DryRun;
        var planSummary = bs.Summary;

        if (!isDryRun && await bs.BackendManager.SupportsFolderDeleteAsync(ct).ConfigureAwait(false))
            Logging.Log.WriteInformationMessage(LOGTAG, logId, "Now deleting {0} {1}", description, relPath);

        var result = await deleteTree(bs.BackendManager, relPath, isDryRun, ct).ConfigureAwait(false);

        planSummary.Delete += result.FilesDeleted;
        planSummary.SizeOfDeletedFiles += result.SizeOfDeletedFiles;
        planSummary.DeleteFailures += result.FilesFailed;
        if (result.FolderRemoved)
            planSummary.FoldersDeleted++;

        if (isDryRun)
        {
            if (result.FolderRemoved)
                Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would delete {0}: {1}", description, relPath);
            else if (result.FilesDeleted > 0 && result.EntriesKept > 0)
                Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would delete the {0} files of {1} {2}; {3} entries below it that the filter excludes would be kept", result.FilesDeleted, description, relPath, result.EntriesKept);
            else if (result.FilesDeleted > 0)
                Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would delete the {0} files of {1} {2}; the destination cannot delete folders, so the empty folders would be left behind", result.FilesDeleted, description, relPath);
        }
        else if (result.FolderRemoved)
        {
            Logging.Log.WriteInformationMessage(LOGTAG, logId, "Deleted {0} {1}", description, relPath);
        }
        else if (result.FilesDeleted > 0)
        {
            if (result.EntriesKept > 0)
                Logging.Log.WriteInformationMessage(LOGTAG, logId, "Deleted the {0} files of {1} {2}; {3} entries below it that the filter excludes are kept", result.FilesDeleted, description, relPath, result.EntriesKept);
            else
                Logging.Log.WriteInformationMessage(LOGTAG, logId, "Deleted the {0} files of {1} {2}; the destination cannot delete folders, so the empty folders are left behind", result.FilesDeleted, description, relPath);
        }

        return result;
    }

    /// <summary>
    /// Prepares a snapshot-style run on one destination: reads the destination root,
    /// chooses the name of this run's snapshot folder (after every existing one, even if
    /// the clock went back or two runs started in the same second) and creates it, with
    /// one sub-folder per source when there are several sources.
    /// </summary>
    /// <param name="mgr">The destination</param>
    /// <param name="stateMode">The resolved remote-state mode for the destination</param>
    /// <param name="runStartUtc">The time the run started</param>
    /// <param name="isDryRun">Whether this is a dry run</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The snapshot state of the run</returns>
    private async Task<SnapshotRunState> PrepareSnapshotAsync(SyncTarget mgr, SyncRemoteState stateMode, DateTime runStartUtc, bool isDryRun, CancellationToken ct)
    {
        var root = await SyncSnapshotLayout.ReadRootStateAsync(mgr.BackendManager, ct).ConfigureAwait(false);

        var name = SyncSnapshotLayout.FormatFolderName(runStartUtc);
        var newest = root.Snapshots.Count > 0 ? root.Snapshots[^1] : null;
        if (newest != null && string.CompareOrdinal(name, newest) <= 0 && SyncSnapshotLayout.TryParseFolderName(newest, out var newestUtc))
        {
            var adjusted = SyncSnapshotLayout.FormatFolderName(newestUtc.AddSeconds(1));
            // Two runs within the same second are normal; an existing snapshot from the
            // future means the clock went back, which the user should know about.
            if (newestUtc > runStartUtc.AddMinutes(1))
                Logging.Log.WriteWarningMessage(LOGTAG, "SnapshotNameAdjusted", null, "The snapshot folder {0} would not sort after the existing snapshot {1}; using {2} instead. Check the system clock.", name, newest, adjusted);
            else
                Logging.Log.WriteInformationMessage(LOGTAG, "SnapshotNameAdjusted", "The snapshot folder {0} would not sort after the existing snapshot {1}; using {2} instead.", name, newest, adjusted);
            name = adjusted;
        }

        // BlindlyUpload uploads everything and never looks at what the destination holds,
        // so it has no previous snapshot to link from.
        var previous = stateMode == SyncRemoteState.BlindlyUpload ? null : root.Latest;

        if (isDryRun)
        {
            Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would create snapshot folder: {0}", name);
        }
        else
        {
            await mgr.BackendManager.EnsureFolderAsync(name, ct).ConfigureAwait(false);
            foreach (var src in m_sources)
            {
                var relRoot = GetSourceRelativeRoot(src, m_sources);
                if (!string.IsNullOrEmpty(relRoot))
                    await mgr.BackendManager.EnsureFolderAsync(SyncSnapshotLayout.Combine(name, relRoot), ct).ConfigureAwait(false);
            }
        }

        Logging.Log.WriteInformationMessage(LOGTAG, "SnapshotStarted", "Writing snapshot {0}; previous snapshot: {1}", name, previous ?? "none");
        return new SnapshotRunState { Name = name, Previous = previous, Root = root, LinksUnavailable = m_options.SyncSnapshotDisableHardlinks };
    }

    /// <summary>
    /// Records which source roots exist in the previous snapshot, so that the folder loop
    /// lists only previous folders that exist. With one source the snapshot folder itself
    /// is the source root; with several, each source has its own sub-folder.
    /// </summary>
    /// <param name="backendManager">The destination</param>
    /// <param name="snapshot">The snapshot state of the run</param>
    /// <param name="sources">The sources of the run</param>
    /// <param name="ct">The cancellation token</param>
    private static async Task SeedPreviousFoldersAsync(IBackendManager backendManager, SnapshotRunState snapshot, string[] sources, CancellationToken ct)
    {
        if (snapshot.Previous == null)
            return;

        if (sources.Length == 1)
        {
            snapshot.PreviousFolders.Add("");
            return;
        }

        foreach (var entry in await backendManager.ListAsync(snapshot.Previous, ct).ConfigureAwait(false))
        {
            var name = entry.Name?.TrimEnd('/');
            if (entry.IsFolder && IsValidEntryName(name))
                snapshot.PreviousFolders.Add(name!);
        }
    }

    /// <summary>
    /// Applies the already-enumerated local state of a single folder to one destination
    /// in snapshot mode. The folder in this run's snapshot starts empty, so every
    /// sub-folder is created and every file is placed: a file that is unchanged since the
    /// previous snapshot is hard linked from it when the destination can link, and every
    /// other file is uploaded. Nothing is ever deleted inside a snapshot.
    /// </summary>
    private static async Task ProcessSnapshotFolderForBackendAsync(
        string folderRelPath,
        Dictionary<string, (ISourceProviderEntry Entry, long Size, DateTime ModifiedUtc)> localFiles,
        List<(ISourceProviderEntry Entry, string RelativePath)> enqueuedSubfolders,
        BackendRunState bs,
        SnapshotRunState snapshot,
        RunContext run,
        StringComparer nameComparer,
        bool verifyHash,
        CancellationToken ct)
    {
        var backendManager = bs.BackendManager;
        var db = bs.Database;
        var stateMode = bs.StateMode;
        var maintainInventory = bs.MaintainInventory;
        var isDryRun = bs.DryRun;
        var planSummary = bs.Summary;

        // 1. What the previous snapshot holds in this folder, keyed by name. Under
        // UseRemoteState the previous folder is listed (when it is known to exist); under
        // UseLocalState the inventory rows of the previous snapshot are read.
        var previousFiles = new Dictionary<string, RemoteChild>(nameComparer);
        if (snapshot.Previous != null)
        {
            var previousFolder = SyncSnapshotLayout.Combine(snapshot.Previous, folderRelPath);
            if (stateMode == SyncRemoteState.UseRemoteState && snapshot.PreviousFolders.Contains(folderRelPath))
            {
                foreach (var re in await backendManager.ListAsync(previousFolder, ct).ConfigureAwait(false))
                {
                    var name = re.Name;
                    if (re.IsFolder && name != null && name.Length > 1 && name[^1] == '/')
                        name = name[..^1];
                    if (string.IsNullOrWhiteSpace(name) || !IsValidEntryName(name))
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "InvalidRemoteName", null, "Skipping remote entry with an unsafe name (contains path separators or parent-directory segments): {0}", name);
                        continue;
                    }

                    if (re.IsFolder)
                        snapshot.PreviousFolders.Add(string.IsNullOrEmpty(folderRelPath) ? name : folderRelPath + "/" + name);
                    else
                        previousFiles[name] = new RemoteChild(name, Math.Max(re.Size, 0), AsUtc(re.LastModification), null);
                }

                // The listing has no hashes; the inventory remembers the hashes of files
                // this job uploaded, which a hash re-check needs.
                if (verifyHash && maintainInventory)
                    await foreach (var inv in db.GetInventoryItemsInFolderAsync(previousFolder, ct).ConfigureAwait(false))
                    {
                        var invName = GetEntryName(inv.RelativePath);
                        if (!string.IsNullOrEmpty(inv.ContentHash) && previousFiles.TryGetValue(invName, out var rc) && rc.Size == inv.Size)
                            previousFiles[invName] = rc with { ContentHash = inv.ContentHash };
                    }
            }
            else if (stateMode == SyncRemoteState.UseLocalState)
            {
                await foreach (var inv in db.GetInventoryItemsInFolderAsync(previousFolder, ct).ConfigureAwait(false))
                {
                    var name = GetEntryName(inv.RelativePath);
                    if (!string.IsNullOrEmpty(name))
                        previousFiles[name] = new RemoteChild(name, inv.Size, inv.LastModified, inv.ContentHash);
                }
            }
        }

        // 2. The folder in this run's snapshot is new, so every sub-folder is created.
        foreach (var (_, subRelPath) in enqueuedSubfolders)
        {
            var target = SyncSnapshotLayout.Combine(snapshot.Name, subRelPath);
            if (isDryRun)
                Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would create folder: {0}", target);
            else
                await backendManager.EnsureFolderAsync(target, ct).ConfigureAwait(false);
            planSummary.FoldersCreated++;
        }

        // 3. Place every file: link the unchanged ones, upload the rest.
        foreach (var (relPath, (entry, size, modifiedUtc)) in localFiles)
        {
            ct.ThrowIfCancellationRequested();

            var name = GetEntryName(relPath);
            var targetPath = SyncSnapshotLayout.Combine(snapshot.Name, relPath);
            previousFiles.TryGetValue(name, out var previous);

            // A destination that keeps the local time may store it in whole seconds (WebDAV)
            var localModifiedUtc = run.KeepModificationTime
                ? Library.Utility.Utility.NormalizeDateTime(modifiedUtc)
                : modifiedUtc;
            var unchanged = previous != null && previous.Size == size && previous.LastModified >= localModifiedUtc;
            if (unchanged && verifyHash && !string.IsNullOrEmpty(previous!.ContentHash))
            {
                try
                {
                    using var stream = await entry.OpenRead(ct).ConfigureAwait(false);
                    using var hasher = HashFactory.CreateHasher("SHA256");
                    unchanged = Convert.ToBase64String(hasher.ComputeHash(stream)) == previous.ContentHash;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FailedToReadFileHash", ex, "Failed to read local file hash for: {0}", relPath);
                    unchanged = false;
                }
            }

            if (unchanged && !snapshot.LinksUnavailable
                && TryBuildSafeRelativePath(SyncSnapshotLayout.Combine(snapshot.Previous!, folderRelPath), previous!.Name, out var previousPath)
                && await backendManager.SupportsHardLinksAsync(ct).ConfigureAwait(false))
            {
                if (isDryRun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would link: {0}", targetPath);
                    planSummary.Linked++;
                    planSummary.UnchangedFiles++;
                    continue;
                }

                try
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "SyncLink", "Linking unchanged file {0} to {1}", targetPath, previousPath);
                    await backendManager.CreateHardLinkAsync(previousPath, targetPath, ct).ConfigureAwait(false);
                    if (maintainInventory)
                        await db.UpsertInventoryAsync(targetPath, previous.Size, previous.LastModified, previous.ContentHash, ct).ConfigureAwait(false);
                    planSummary.Linked++;
                    planSummary.UnchangedFiles++;
                    run.ReportProcessed(bs);
                    continue;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested && Library.Utility.ExceptionExtensions.FlattenException(ex).Any(x => x is HardLinkNotSupportedException))
                {
                    snapshot.LinksUnavailable = true;
                    Logging.Log.WriteWarningMessage(LOGTAG, "HardLinksUnavailable", ex, "The destination cannot create hard links, so unchanged files are copied into the snapshot again: {0}", ex.Message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "LinkFailed", ex, "Failed to link {0} from the previous snapshot; uploading it instead", relPath);
                }
            }

            // A file the previous snapshot has in a changed version is an update; a new
            // file, or an unchanged file that could not be linked, is an upload.
            var operation = previous != null && !unchanged ? SyncOperation.Update : SyncOperation.Upload;
            var label = operation == SyncOperation.Update ? "Updating" : "Uploading";
            try
            {
                await UploadOrUpdateAsync(bs, run, targetPath, entry, operation, label, verifyHash, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
            {
                planSummary.UploadFailures++;
                Logging.Log.WriteWarningMessage(LOGTAG, "UploadFailed", ex, "Failed to {0} file: {1}", label, targetPath);
            }
        }
    }

    /// <summary>
    /// Completes a snapshot-style run on one destination: marks this run's snapshot as the
    /// latest, drops the inventory rows of every other snapshot, and removes the folders
    /// of interrupted runs and the snapshots beyond <paramref name="keepSnapshots"/>.
    /// Nothing is removed when a file of this run failed, so a damaged snapshot never
    /// pushes a good one out.
    /// </summary>
    /// <param name="bs">The destination state</param>
    /// <param name="keepSnapshots">The number of snapshots to keep; zero keeps all</param>
    /// <param name="ct">The cancellation token</param>
    private static async Task FinishSnapshotAsync(BackendRunState bs, int keepSnapshots, CancellationToken ct)
    {
        var isDryRun = bs.DryRun;
        var planSummary = bs.Summary;
        var snapshot = bs.Snapshot;
        if (snapshot == null)
            return;

        if (isDryRun)
        {
            Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would mark snapshot {0} as the latest", snapshot.Name);
        }
        else
        {
            await SyncSnapshotLayout.WriteMarkerAsync(bs.BackendManager, snapshot.Name, ct).ConfigureAwait(false);
            await bs.Database.RemoveInventoryOutsideFolderAsync(snapshot.Name, ct).ConfigureAwait(false);
        }

        // Folders newer than the latest completed snapshot belong to runs that stopped
        // before they finished.
        var latestBefore = snapshot.Root.Latest;
        var aborted = latestBefore == null
            ? []
            : snapshot.Root.Snapshots.Where(x => string.CompareOrdinal(x, latestBefore) > 0).ToList();

        var completed = snapshot.Root.CompletedSnapshots.Append(snapshot.Name).ToList();
        var expired = keepSnapshots > 0 && completed.Count > keepSnapshots
            ? completed.Take(completed.Count - keepSnapshots).ToList()
            : [];

        if (aborted.Count == 0 && expired.Count == 0)
            return;

        if (planSummary.UploadFailures > 0)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "SnapshotRetentionSkipped", null, "{0} files could not be written to snapshot {1}, so no older snapshot is removed in this run.", planSummary.UploadFailures, snapshot.Name);
            return;
        }

        foreach (var folder in aborted.Concat(expired))
        {
            var reason = aborted.Contains(folder) ? "incomplete" : "expired";
            try
            {
                await DeleteFolderTreeAsync(bs, folder, $"{reason} snapshot", "SnapshotDeleted", SyncSnapshotLayout.DeleteSnapshotAsync, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
            {
                // Counted like a file that cannot be deleted one by one
                planSummary.DeleteFailures++;
                Logging.Log.WriteWarningMessage(LOGTAG, "SnapshotDeleteFailed", ex, "Failed to delete snapshot {0}: {1}", folder, ex.Message);
            }
        }
    }

    /// <summary>
    /// Returns true if the inventory has any file row under the given folder, which
    /// is the cheap local-database evidence that the folder exists on the remote.
    /// Used under UseLocalState to avoid an EnsureFolderAsync call for folders that
    /// already contain uploaded files.
    /// </summary>
    private static async Task<bool> FolderHasInventoryAsync(LocalSyncDatabase db, string folderRelPath, CancellationToken ct)
    {
        await foreach (var _ in db.GetInventoryItemsInFolderAsync(folderRelPath, ct).ConfigureAwait(false))
            return true;
        return false;
    }

    /// <summary>
    /// Uploads (or updates) a single local file to the remote destination, recording
    /// intent before the upload and committing the inventory update + intent clearance
    /// atomically after it. The caller MUST have ensured the destination folder exists
    /// already; this method does not call <see cref="IBackendManager.EnsureFolderAsync"/>.
    /// Shared by the upload and update paths so the two cannot drift.
    /// </summary>
    /// <param name="bs">The destination state.</param>
    /// <param name="run">The run context whose progress updater receives per-file progress.</param>
    /// <param name="relPath">The relative path on the remote destination.</param>
    /// <param name="lf">The local source entry to upload.</param>
    /// <param name="operation">The intent operation to record (<see cref="SyncOperation.Upload"/> or <see cref="SyncOperation.Update"/>).</param>
    /// <param name="logLabel">The label used in the log message (e.g. "Uploading" or "Updating").</param>
    /// <param name="verifyHash">Whether to compute and store a content hash.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    private static async Task UploadOrUpdateAsync(
        BackendRunState bs,
        RunContext run,
        string relPath,
        ISourceProviderEntry lf,
        SyncOperation operation,
        string logLabel,
        bool verifyHash,
        CancellationToken cancellationToken)
    {
        var db = bs.Database;
        var backendManager = bs.BackendManager;
        var maintainInventory = bs.MaintainInventory;
        var isDryRun = bs.DryRun;
        var planSummary = bs.Summary;

        if (isDryRun)
        {
            Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would {0}: {1}", logLabel.ToLowerInvariant(), relPath);
            if (operation == SyncOperation.Upload)
                planSummary.Upload++;
            else
                planSummary.Update++;
            planSummary.SizeOfUploadedFiles += Math.Max(lf.Size, 0);
            return;
        }

        Logging.Log.WriteInformationMessage(LOGTAG, logLabel, "{0} {1}", logLabel, relPath);

        // Tell the progress reporter we are starting on this file so the UI can show
        // the current filename and the total size we expect to write for it. The
        // matching UpdatefilesProcessed below advances the processed count by one
        // file and its size once the upload (or its failure) is accounted for.
        run.Progress.StartFile(relPath, Math.Max(lf.Size, 0));

        // Record intent BEFORE the upload so a crash during the put is recoverable.
        // This row is cleared at the start of the next run (the journal is reconciled
        // per folder against a fresh listing under UseRemoteState, or cleared outright).
        await db.UpsertPendingOperationAsync(relPath, operation, lf.Size, null, cancellationToken);

        using var temp = new TempFile();
        using (var s = await lf.OpenRead(cancellationToken))
        using (var fs = System.IO.File.OpenWrite(temp))
            await s.CopyToAsync(fs, cancellationToken);

        string? hash = null;
        if (verifyHash)
        {
            using var hashStream = System.IO.File.OpenRead(temp);
            using var hasher = HashFactory.CreateHasher("SHA256");
            hash = Convert.ToBase64String(hasher.ComputeHash(hashStream));
        }

        // The caller has already ensured the destination folder exists, so we put the
        // file directly. No EnsureFolderAsync call here.
        await backendManager.PutFileUnencryptedWithPathAsync(relPath, temp, run.KeepModificationTime ? lf.LastModificationUtc : null, cancellationToken);

        // Commit the inventory update and intent clearance atomically so a crash
        // between them cannot leave the inventory and intent journal inconsistent.
        // Under UseRemoteState/BlindlyUpload (maintainInventory == false) the inventory
        // upsert is skipped; the intent clearance still runs so the journal stays clean.
        await db.ExecuteInTransactionAsync(async txCt =>
        {
            if (maintainInventory)
                await db.UpsertInventoryAsync(relPath, lf.Size, lf.LastModificationUtc, hash, txCt);
            await db.RemovePendingOperationAsync(relPath, txCt);
        }, cancellationToken);

        if (operation == SyncOperation.Upload)
            planSummary.Upload++;
        else
            planSummary.Update++;
        planSummary.SizeOfUploadedFiles += Math.Max(lf.Size, 0);

        // The file has been fully processed (uploaded or its attempt is accounted for),
        // so advance the processed-files counter by one file and its size. This pairs
        // with the StartFile call above and lets the UI show N of M files processed.
        run.ReportProcessed(bs);
    }

    /// <summary>
    /// Brings a timestamp from a backend listing to UTC. Backends do not agree on the
    /// kind of the DateTime they report: most report UTC, the file backend reports
    /// local time, and some parse a date without a zone and report it as unspecified.
    /// A local time is converted; an unspecified kind is taken as UTC, which is what
    /// the backends that produce it mean by it. Comparing without this is off by the
    /// zone offset, and makes unchanged files look newer and be copied again.
    /// </summary>
    /// <param name="value">The timestamp to convert</param>
    /// <returns>The timestamp in UTC</returns>
    internal static DateTime AsUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };

    /// <summary>
    /// Finds the folder entry of a source root. A local source is looked up by its path.
    /// A remote source ("@mountpoint|url") is not a path the providers know: its root is
    /// the root that the provider mounted at its mount point lists, so the backend is not
    /// asked to look up a single entry, which some backends (e.g. SSH) cannot do.
    /// </summary>
    /// <param name="sourceProvider">The source provider of the run</param>
    /// <param name="source">The source as given</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The entry of the source root, or null if it was not found</returns>
    internal static async Task<ISourceProviderEntry?> GetSourceRootAsync(ISourceProvider sourceProvider, string source, CancellationToken ct)
    {
        var remoteSource = SourceProviderFactory.ParseRemoteSource(source);
        if (remoteSource == null)
            return await sourceProvider.GetEntryAsync(source, true, ct).ConfigureAwait(false);

        var mountpoint = SourceProviderFactory.NormalizeMountpoint(remoteSource.Mountpoint);
        var providers = sourceProvider is Library.SourceProvider.Combiner combiner
            ? combiner.Providers
            : [sourceProvider];

        // A provider that failed to load is not in the list; a warning is logged for it already
        var provider = providers.FirstOrDefault(x => string.Equals(x.MountedPath, mountpoint, StringComparison.Ordinal));
        if (provider == null)
            return null;

        await foreach (var entry in provider.EnumerateAsync(ct).ConfigureAwait(false))
            if (entry.IsFolder)
                return entry;

        return null;
    }

    /// <summary>
    /// Gets a source as it can be shown in a log message, without the credentials in the URL of a remote source
    /// </summary>
    /// <param name="source">The source as given</param>
    /// <returns>The source without credentials</returns>
    private static string GetSourceWithoutCredentials(string source)
    {
        var remoteSource = SourceProviderFactory.ParseRemoteSource(source);
        return remoteSource == null
            ? source
            : "@" + remoteSource.Mountpoint + "|" + Library.Utility.Utility.GetUrlWithoutCredentials(remoteSource.Url);
    }

    /// <summary>
    /// Computes the relative root path on the remote destination for a source root,
    /// matching the relative-path convention used throughout the handler. For a
    /// single source the source's contents map directly onto the backend root
    /// (relative root ""); for multiple sources each source contributes its own name
    /// as a top-level sub-folder. A remote source ("@mountpoint|url") is named after
    /// its mount point, as the URL is not a path and may hold credentials.
    /// </summary>
    internal static string GetSourceRelativeRoot(string sourcePath, string[] sources)
    {
        if (sources.Length == 1)
            return "";

        var remoteSource = SourceProviderFactory.ParseRemoteSource(sourcePath);
        if (remoteSource != null)
            sourcePath = remoteSource.Mountpoint;

        var normalized = sourcePath.Replace('\\', '/').TrimEnd('/');
        var sourceName = Path.GetFileName(normalized);
        return sourceName ?? "";
    }

    /// <summary>
    /// Extracts the entry name (the last path component) from a source-provider entry
    /// path, normalizing backslashes to forward slashes and stripping any trailing
    /// separators first (folder entries from the snapshot service carry a trailing
    /// directory separator, which would otherwise yield an empty name). Returns the
    /// empty string if the path has no name component.
    /// </summary>
    private static string GetEntryName(string entryPath)
    {
        var normalized = entryPath.Replace('\\', '/').TrimEnd('/');
        var idx = normalized.LastIndexOf('/');
        return idx < 0 ? normalized : normalized.Substring(idx + 1);
    }

    /// <summary>
    /// Validates that a single entry name (a remote or local child name, with no
    /// path separators) is safe to use as a component of a relative path on the
    /// remote destination. Rejects names that could escape the synced tree or break
    /// the path bookkeeping: parent-directory segments (<c>..</c>), empty names,
    /// names containing path separators (the relative path is built by joining with
    /// <c>/</c>, so a separator in a name would silently inject a sub-folder), and
    /// names containing backslashes (which a Windows backend may treat as a
    /// separator). Backends are no longer expected to create missing parent folders
    /// and folder-enabled backends (e.g. the File backend) join the relative path
    /// onto their root, so a <c>..</c> or absolute name from a malicious/compromised
    /// backend or a malformed local name could otherwise delete or upload outside
    /// the configured destination. Returns <c>true</c> when the name is safe.
    /// </summary>
    /// <param name="name">The entry name to validate.</param>
    /// <returns><c>true</c> when the name is a safe single path component; <c>false</c> otherwise.</returns>
    private static bool IsValidEntryName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (name == "." || name == "..")
            return false;
        if (name!.Contains('/') || name.Contains('\\'))
            return false;
        return true;
    }

    /// <summary>
    /// Validates that a relative path built from a folder's relative path and a
    /// child entry name is safe to use on the remote destination. The folder
    /// relative path is itself validated for traversal segments, and the child name
    /// is validated via <see cref="IsValidEntryName"/>. Returns <c>true</c> when the
    /// resulting relative path is safe; <c>false</c> otherwise (and the caller should
    /// skip the entry with a warning rather than carry it through to the backend).
    /// </summary>
    /// <param name="folderRelPath">The folder's relative path (may be empty for the backend root).</param>
    /// <param name="childName">The child entry name to append.</param>
    /// <param name="relPath">When safe, the joined relative path; otherwise null.</param>
    /// <returns><c>true</c> when the joined path is safe; <c>false</c> otherwise.</returns>
    private static bool TryBuildSafeRelativePath(string folderRelPath, string? childName, out string relPath)
    {
        relPath = string.Empty;
        if (!IsValidEntryName(childName))
            return false;
        // The folder relative path is constructed by this handler from previously
        // validated names, but validate defensively in case a future caller supplies
        // an unvalidated folder path.
        if (folderRelPath.Length > 0)
        {
            foreach (var segment in folderRelPath.Split('/'))
                if (string.IsNullOrEmpty(segment) || segment == "." || segment == "..")
                    return false;
        }
        relPath = string.IsNullOrEmpty(folderRelPath) ? childName! : folderRelPath + "/" + childName!;
        return true;
    }

    /// <summary>
    /// A queued folder awaiting per-folder processing: the local source entry to
    /// enumerate and the relative path on the remote destination that it maps to.
    /// </summary>
    private sealed record FolderWorkItem(ISourceProviderEntry Entry, string RelativePath, int Depth);

    /// <summary>
    /// A remote child entry (file) observed in a folder, as returned by a listing or
    /// read from the inventory cache. Keyed by name within the folder.
    /// </summary>
    private sealed record RemoteChild(string Name, long Size, DateTime LastModified, string? ContentHash);

    /// <summary>
    /// The state shared by the whole run: the per-destination states, the counters
    /// of the single source enumeration, and the progress reporting.
    /// </summary>
    private sealed class RunContext(IOperationProgressUpdater progress, Action<int, int>? onTargetStarted, bool perTargetProgress, bool countFromListing, bool keepModificationTime)
    {
        /// <summary>The per-destination states, in the order the destinations were given</summary>
        public List<BackendRunState> States { get; } = [];
        /// <summary>The progress updater of the run</summary>
        public IOperationProgressUpdater Progress => progress;
        /// <summary>Whether the file count is reported from the folder listing instead of a background count</summary>
        public bool CountFromListing => countFromListing;
        /// <summary>Whether the modification time of the source file is recorded on the remote file, where the backend supports it</summary>
        public bool KeepModificationTime => keepModificationTime;
        /// <summary>The number of source files seen</summary>
        public long SourceFiles;
        /// <summary>The size of the source files seen</summary>
        public long SizeOfSourceFiles;

        private int m_currentTarget = -1;

        /// <summary>
        /// Notes that the work moves to the destination with the given index
        /// </summary>
        /// <param name="index">The index of the destination</param>
        public void EnterTarget(int index)
        {
            if (m_currentTarget == index)
                return;
            m_currentTarget = index;
            onTargetStarted?.Invoke(index, States.Count);
        }

        /// <summary>
        /// Reports the number of files handled so far. A stand-alone sync counts across
        /// all destinations; a run embedded in another operation reports the destination
        /// being worked on, which is what that operation shows.
        /// </summary>
        /// <param name="current">The destination being worked on</param>
        /// <param name="includeDeletes">Whether deleted files count as handled</param>
        public void ReportProcessed(BackendRunState current, bool includeDeletes = false)
        {
            long count = 0, size = 0;
            foreach (var ps in perTargetProgress ? [current.Summary] : States.Select(x => x.Summary))
            {
                count += ps.Upload + ps.Update + ps.UnchangedFiles + (includeDeletes ? ps.Delete : 0);
                size += ps.SizeOfUploadedFiles + (includeDeletes ? ps.SizeOfDeletedFiles : 0);
            }
            progress.UpdatefilesProcessed(count, size);
        }
    }

    /// <summary>
    /// Per-backend run state for the shared single-enumeration sync: the backend
    /// manager + database pair, the resolved <see cref="SyncRemoteState"/> for this
    /// run (after the UseLocalState-fallback is applied), whether the inventory
    /// cache is maintained for this backend, whether the destination is only
    /// simulated, and the counters of the destination. Computed once per backend
    /// before the shared folder loop so the per-folder fan-out applies the same
    /// observed local state to every backend without re-resolving (or
    /// re-enumerating) per backend.
    /// </summary>
    private sealed record BackendRunState(
        IBackendManager BackendManager,
        LocalSyncDatabase Database,
        SyncRemoteState StateMode,
        bool MaintainInventory,
        SnapshotRunState? Snapshot,
        bool DryRun,
        SyncTargetSummary Summary
    );

    /// <summary>
    /// Per-backend state of a snapshot-style run (<see cref="Options.SyncSnapshotFolders"/>).
    /// Every remote path of the run is below <see cref="Name"/>; the previous snapshot is
    /// the baseline that decides which files are unchanged and can be linked.
    /// </summary>
    private sealed class SnapshotRunState
    {
        /// <summary>
        /// The snapshot folder this run writes into
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The latest completed snapshot before this run, or <c>null</c> if there is none
        /// or the state mode has no remote state to compare with
        /// </summary>
        public required string? Previous { get; init; }

        /// <summary>
        /// The destination root as it was before this run
        /// </summary>
        public required SyncSnapshotLayout.RootState Root { get; init; }

        /// <summary>
        /// The folders (relative to the snapshot) that a listing has shown to exist in the
        /// previous snapshot. A folder is listed only when it is known to exist there,
        /// since listing a missing folder fails after the backend's retries.
        /// </summary>
        public HashSet<string> PreviousFolders { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Set when hard links are turned off with
        /// <see cref="Options.SYNC_SNAPSHOT_DISABLE_HARDLINKS_OPTION"/>, or once the
        /// destination has refused a hard link; unchanged files are then uploaded again
        /// for the rest of the run.
        /// </summary>
        public bool LinksUnavailable { get; set; }
    }
}