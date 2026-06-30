using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database.Sync;
using Duplicati.Library.Utility;
using Duplicati.Library.Main.Operation.Backup;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Backend;

#nullable enable

namespace Duplicati.Library.Main.Operation.Sync;

internal class SyncHandler
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<SyncHandler>();
    private readonly Options m_options;
    private readonly SyncResults m_results;
    private readonly string[] m_sources;
    private readonly string? m_backendUrl;

    public SyncHandler(string[] sources, Options options, SyncResults results, string? backendUrl = null)
    {
        m_sources = sources;
        m_options = options;
        m_results = results;
        m_backendUrl = backendUrl;
    }

    public async Task RunAsync(IBackendManager backendManager, IFilter filter)
    {
        var ct = m_results.TaskControl.ProgressToken;
        m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Sync_Begin);
        Logging.Log.WriteInformationMessage(LOGTAG, "SyncStarted", "Starting sync");

        // Sync mirrors files to the destination unencrypted, so a passphrase is
        // meaningless here and must never be used.
        if (!m_options.NoEncryption)
            throw new UserInformationException("Encryption must be disabled for sync as it is not supported.", "EncryptionNotAllowed");

        var primaryDbPath = m_options.Dbpath
            ?? throw new InvalidOperationException("Unable to locate sync database");

        var isDryRun = m_options.Dryrun;
        var isDelete = m_options.SyncThenDelete;
        var verifyHash = m_options.SyncVerifyHash;
        var configuredStateMode = m_options.SyncRemoteState;

        // --sync-recheck forces a fresh remote listing per folder for this run. Under
        // UseLocalState that also means the local inventory cache is treated as stale:
        // the run effectively behaves as UseRemoteState, and (because we keep the
        // inventory write-through under UseLocalState) the cache is refreshed as we
        // go, so the next UseLocalState run starts from a fresh baseline. Under
        // UseRemoteState and BlindlyUpload the flag is a no-op (UseRemoteState already
        // lists fresh; BlindlyUpload ignores remote state entirely).
        var initialStateMode = (configuredStateMode == SyncRemoteState.UseLocalState && m_options.SyncRecheck)
            ? SyncRemoteState.UseRemoteState
            : configuredStateMode;

        // BlindlyUpload has no remote state to diff against, so deleting remote files
        // is not meaningful. Warn once at the start so the user is not surprised that
        // --sync-then-delete is ignored for the run.
        if (initialStateMode == SyncRemoteState.BlindlyUpload && isDelete)
            Logging.Log.WriteWarningMessage(LOGTAG, "BlindlyUploadIgnoresDelete", null, $"sync-remote-state={SyncRemoteState.BlindlyUpload} cannot determine which remote files to delete; --sync-then-delete will be ignored for this run.");

        using var sourceProvider = await SourceProviderFactory.GetSourceProviderAsync(m_sources, m_options, ct).ConfigureAwait(false);

        var blacklistPaths = BackupHandler.GetBlacklistedPaths(m_options);
        var fileAttributeFilter = m_options.FileAttributeFilter;
        var symlinkPolicy = m_options.SymlinkPolicy;
        var hardlinkPolicy = m_options.HardlinkPolicy;
        var disableBackupExclusionXattr = m_options.DisableBackupExclusionXattr;
        var excludeEmptyFolders = m_options.ExcludeEmptyFolders;
        var ignoreNames = m_options.IgnoreFilenames;

        // Start a parallel background counter that walks the whole source tree
        using var counterCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? counterTask = null;
        if (!m_options.DisableFileScanner)
        {
            m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Sync_CountingFiles);
            counterTask = CountFilesHandler.RunAsync(
                sourceProvider,
                journalService: null,
                m_results,
                fileAttributeFilter,
                filter,
                symlinkPolicy,
                hardlinkPolicy,
                disableBackupExclusionXattr,
                excludeEmptyFolders,
                ignoreNames,
                blacklistPaths,
                counterCts.Token);
        }

        var planSummary = new PlanSummary();
        var additionalManagers = CreateAdditionalBackendManagers();
        var allManagers = new List<ManagerInstance>([
            new ManagerInstance(backendManager, new LocalSyncDatabase(primaryDbPath)),
            .. additionalManagers
            ]);

        // Per-backend run state: each destination has its own database (its own inventory
        // and intent journal) and its own per-run state-mode resolution (the UseLocalState
        // fallback depends on whether THIS backend's inventory is empty). Computing these
        // once per backend before the shared folder loop keeps the per-folder fan-out cheap
        // and makes a single source enumeration feed every backend, so the same observed
        // local state is replicated to all destinations instead of re-enumerating the
        // source once per backend (where the source could change between enumerations).
        var backendStates = new List<BackendRunState>(allManagers.Count);
        try
        {
            // Collect all initial states from all managers
            foreach (var mgr in allManagers)
            {
                var stateMode = initialStateMode;
                var forcePopulateInventory = false;

                // Resume: leftover PendingOperation rows mean a previous run was interrupted
                // mid-operation. Under UseRemoteState each folder is listed fresh this run, so
                // any in-flight intent is naturally superseded by the live remote state and the
                // leftover rows can simply be cleared. Under UseLocalState the inventory may be
                // stale for the interrupted paths; we clear the rows but warn the user that a
                // recheck (UseRemoteState) is the way to reconcile fully. Under BlindlyUpload
                // the journal is never consulted, so leftover rows are just noise; clear them.
                if (await mgr.Database.HasAnyPendingOperationsAsync(ct).ConfigureAwait(false))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "InflightDetected", null, "Detected incomplete operations from a previous run. Clearing the intent journal; {0}.", stateMode == SyncRemoteState.UseLocalState ? "run with --sync-recheck (or sync-remote-state=use-remote-state) to reconcile against a fresh remote listing" : "the current run will re-establish remote state per folder");
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
                var maintainInventory = stateMode == SyncRemoteState.UseLocalState
                    || (stateMode == SyncRemoteState.UseRemoteState && verifyHash)
                    || forcePopulateInventory;

                backendStates.Add(new BackendRunState(mgr.BackendManager, mgr.Database, stateMode, maintainInventory));
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
                var entry = await sourceProvider.GetEntryAsync(src, true, ct).ConfigureAwait(false);
                if (entry == null || !entry.IsFolder)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SourceRootMissing", null, "Source folder could not be resolved, skipping: {0}", src);
                    continue;
                }
                folderQueue.Enqueue(new FolderWorkItem(entry, relRoot));
            }

            m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Sync_ProcessingFiles);
            while (folderQueue.Count > 0)
            {
                if (ct.IsCancellationRequested)
                    break;

                var current = folderQueue.Dequeue();
                await ProcessFolderAsync(
                    current,
                    backendStates,
                    m_results,
                    filter,
                    fileAttributeFilter,
                    symlinkPolicy,
                    hardlinkPolicy,
                    disableBackupExclusionXattr,
                    excludeEmptyFolders,
                    ignoreNames,
                    blacklistPaths,
                    isDryRun,
                    isDelete,
                    verifyHash,
                    planSummary,
                    folderQueue,
                    ct).ConfigureAwait(false);
            }
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

            foreach (var bm in additionalManagers)
            {
                try
                {
                    bm.BackendManager.Dispose();
                    bm.Database.Dispose();
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BackendDisposeFailed", ex, "Failed to dispose additional backend manager: {0}", ex.Message);
                }
            }
        }

        // The per-folder work is committed; the remaining steps (flushing backend
        // messages, the plan summary log, and the results flush) are bookkeeping
        // rather than uploads, so report the wait-for-upload phase here.
        m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Sync_WaitForUpload);

        Logging.Log.WriteInformationMessage(LOGTAG, "SyncPlan", "Plan: Upload {0}, Update {1}, Delete {2}", planSummary.Upload, planSummary.Update, planSummary.Delete);

        // Flush the running counters into the results so callers (CLI, IPC, web UI)
        // see folders created, files uploaded/unchanged/deleted, and the total
        // files/size encountered. Upload+Update is the combined "files uploaded"
        // count; Delete maps to files deleted from the remote.
        m_results.FoldersCreated = planSummary.FoldersCreated;
        // The handler deletes unknown remote files only, never remote folders, so
        // the folders-deleted count is always zero today; the field is exposed for
        // completeness and future folder-deletion support.
        m_results.FoldersDeleted = 0;
        m_results.FilesUploaded = planSummary.Upload + planSummary.Update;
        m_results.UnchangedFiles = planSummary.UnchangedFiles;
        m_results.FilesDeleted = planSummary.Delete;
        m_results.SourceFiles = planSummary.SourceFiles;
        m_results.SizeOfSourceFiles = planSummary.SizeOfSourceFiles;
        m_results.SizeOfUploadedFiles = planSummary.SizeOfUploadedFiles;
        m_results.SizeOfDeletedFiles = planSummary.SizeOfDeletedFiles;

        m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Sync_Complete);
        // Final tally: the count is no longer in flux, so mark it done.
        m_results.OperationProgressUpdater.UpdatefileCount(m_results.SourceFiles, m_results.SizeOfSourceFiles, true);

        Logging.Log.WriteInformationMessage(LOGTAG, "SyncFinished", "Sync completed successfully.");
    }

    /// <summary>
    /// Parses the <see cref="Options.RemoteSyncJsonConfig" /> value and creates
    /// <see cref="BackendManager" /> instances for any additional destinations.
    /// In this version all options are ignored except the destination URL.
    /// </summary>
    private List<ManagerInstance> CreateAdditionalBackendManagers()
    {
        var managers = new List<ManagerInstance>();
        if (string.IsNullOrWhiteSpace(m_options.RemoteSyncJsonConfig))
            return managers;

        string jsonContent;
        var trimmed = m_options.RemoteSyncJsonConfig.TrimStart();
        if (trimmed.StartsWith("{"))
        {
            jsonContent = m_options.RemoteSyncJsonConfig;
        }
        else
        {
            try
            {
                jsonContent = File.ReadAllText(m_options.RemoteSyncJsonConfig);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncConfigFileReadError", ex, "Failed to read remote sync configuration file '{0}': {1}", m_options.RemoteSyncJsonConfig, ex.Message);
                return managers;
            }
        }

        try
        {
            var deserializeOpts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
            };
            var toplevel = JsonSerializer.Deserialize<TopLevelRemoteSyncConfig>(jsonContent, deserializeOpts);
            if (toplevel == null)
                return managers;

            foreach (var dest in toplevel.Destinations)
            {
                if (!string.IsNullOrWhiteSpace(dest.Url) && !string.Equals(dest.Url, m_backendUrl, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {

                        var bm = new BackendManager(dest.Url, m_options, m_results.BackendWriter, m_results.TaskControl);
                        var db = dest.SyncDatabasePath;
                        if (string.IsNullOrWhiteSpace(db))
                            db = CLIDatabaseLocator.GetDatabasePathForCLI(dest.Url, m_options, true, false, true);
                        if (string.IsNullOrWhiteSpace(db))
                            throw new UserInformationException($"Failed to locate database for {Library.Utility.Utility.GetUrlWithoutCredentials(dest.Url)}", "DatabaseLocateFailed");
                        managers.Add(new ManagerInstance(bm, new LocalSyncDatabase(db)));
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "AdditionalBackendCreateFailed", ex, "Failed to create backend manager for additional target {0}: {1}", Library.Utility.Utility.GetUrlWithoutCredentials(dest.Url), ex.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncConfigParseError", ex, "Failed to parse remote sync JSON configuration: {0}", ex.Message);
        }

        return managers;
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
        IReadOnlyList<BackendRunState> backendStates,
        SyncResults results,
        IFilter filter,
        FileAttributes fileAttributeFilter,
        Options.SymlinkStrategy symlinkPolicy,
        Options.HardlinkStrategy hardlinkPolicy,
        bool disableBackupExclusionXattr,
        bool excludeEmptyFolders,
        string[]? ignoreNames,
        HashSet<string> blacklistPaths,
        bool isDryRun,
        bool isDelete,
        bool verifyHash,
        PlanSummary planSummary,
        Queue<FolderWorkItem> folderQueue,
        CancellationToken ct)
    {
        var folderRelPath = current.RelativePath;
        var folderEntry = current.Entry;

        // 1. Enumerate the local folder's direct children (non-recursive) ONCE and
        // split them into sub-folders (to enqueue) and files (to upload this pass).
        // The filter reuses the backup enumeration filter so attribute/symlink/
        // hardlink/blacklist/xattr/ignore-name behavior is identical between backup
        // and sync. Only this folder's children are resident in memory at once, and
        // the same captured children feed every backend so the source is read a single
        // time for the whole run.
        var localSubfolders = new List<(ISourceProviderEntry Entry, string RelativePath)>();
        var localFiles = new Dictionary<string, (ISourceProviderEntry Entry, long Size, DateTime ModifiedUtc)>(StringComparer.Ordinal);

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
                localSubfolders.Add((child, childRelPath));
            }
            else
            {
                // Count every local source file encountered (regardless of whether it
                // ends up uploaded, unchanged, or de-duplicated) so SourceFiles and
                // SizeOfSourceFiles reflect the full local inventory scanned this run.
                // The source is enumerated once for the whole run, so the count is the
                // true source total regardless of how many backends receive it.
                planSummary.SourceFiles++;
                planSummary.SizeOfSourceFiles += Math.Max(child.Size, 0);

                // INSERT OR IGNORE semantics: the first entry seen for a path wins, so a
                // later duplicate in the same folder is ignored (paths are unique within
                // a single folder in practice).
                if (!localFiles.ContainsKey(childRelPath))
                    localFiles[childRelPath] = (child, child.Size, child.LastModificationUtc);
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
            folderQueue.Enqueue(new FolderWorkItem(subEntry, subRelPath));
        }

        // 3. Replicate the captured local state to each backend. For every backend we
        // resolve that backend's remote state for this folder, ensure any missing
        // sub-folders, upload/update files, and (optionally) delete unknown remote
        // files. The local children (localFiles / enqueuedSubfolders) are shared, so
        // each backend applies the SAME observed source state; the source is not
        // re-enumerated per backend, which keeps the destinations mutually consistent
        // even if the source were to change mid-run.
        foreach (var bs in backendStates)
        {
            await ProcessFolderForBackendAsync(
                folderRelPath,
                localFiles,
                enqueuedSubfolders,
                bs,
                results,
                isDryRun,
                isDelete,
                verifyHash,
                planSummary,
                ct).ConfigureAwait(false);
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
        SyncResults results,
        bool isDryRun,
        bool isDelete,
        bool verifyHash,
        PlanSummary planSummary,
        CancellationToken ct)
    {
        var (backendManager, db, stateMode, maintainInventory) = bs;

        // Check remote state for the current folder. Under UseRemoteState we list
        // the remote folder; under UseLocalState we read the inventory cache; under
        // BlindlyUpload we have no remote state. The remote state is keyed by the
        // child's name (not relative path) for easy lookup against the local files,
        // and a parallel set of remote sub-folder names drives the
        // "create missing folders" step.
        var remoteFileState = new Dictionary<string, RemoteChild>(StringComparer.Ordinal);
        var remoteFolderNames = new HashSet<string>(StringComparer.Ordinal);

        if (stateMode == SyncRemoteState.UseRemoteState)
        {
            var remoteEntries = await backendManager.ListAsync(string.IsNullOrEmpty(folderRelPath) ? null : folderRelPath, ct).ConfigureAwait(false);
            foreach (var re in remoteEntries)
            {
                // Validate the backend-supplied name before using it. A malicious or
                // compromised backend could return a name containing "..", path
                // separators, or a backslash; such a name would otherwise flow into
                // delete/upload/list relative paths and escape the synced destination
                // tree on folder-enabled backends (e.g. the File backend joins the
                // relative path onto its root). Skip the entry with a warning rather
                // than carrying it through.
                if (!IsValidEntryName(re.Name))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "InvalidRemoteName", null, "Skipping remote entry with an unsafe name (contains path separators or parent-directory segments): {0}", re.Name);
                    continue;
                }

                if (re.IsFolder)
                {
                    remoteFolderNames.Add(re.Name);
                    continue;
                }

                // Size is clamped to >= 0 because some backends return -1 to mean
                // "unknown size"; the inventory stores a non-negative size and the
                // diff treats -1 as "do not cross-check size". The last-modification
                // time is normalized to UTC because IFileEntry.LastModification may be
                // Local/Unspecified-kind and the local entry time is UTC; comparing
                // DateTimes of mixed Kind compares raw ticks without conversion, which
                // would treat a local noon as later than a UTC morning and skip updates.
                remoteFileState[re.Name] = new RemoteChild(re.Name, Math.Max(re.Size, 0), re.LastModification.ToUniversalTime(), null);
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
                var invByName = new Dictionary<string, LocalSyncDatabase.InventoryItem>(StringComparer.Ordinal);
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
                    if (remoteEntry.Size != size || remoteEntry.LastModified < modifiedUtc)
                    {
                        needsUpload = true;
                    }
                    else if (verifyHash && !string.IsNullOrEmpty(remoteEntry.ContentHash)
                             && remoteEntry.Size == size && remoteEntry.LastModified >= modifiedUtc)
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
                results.OperationProgressUpdater.UpdatefilesProcessed(planSummary.Upload + planSummary.Update + planSummary.UnchangedFiles, planSummary.SizeOfUploadedFiles);
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
                            results.OperationProgressUpdater.UpdatefilesProcessed(planSummary.Upload + planSummary.Update + planSummary.UnchangedFiles, planSummary.SizeOfUploadedFiles);
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

            try
            {
                await UploadOrUpdateAsync(db, backendManager, results, relPath, entry, operation, label, verifyHash, maintainInventory, isDryRun, planSummary, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
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
                await backendManager.DeleteAsync(relPath, 0, true, ct).ConfigureAwait(false);
                deletedPaths.Add(relPath);
                planSummary.Delete++;
                planSummary.SizeOfDeletedFiles += Math.Max(rc.Size, 0);
            }

            // Advance the processed-files counter by the files deleted in this folder
            // so the UI progress reflects deletes too. Deletes do not contribute to
            // the uploaded size, so only the count side moves relative to uploads.
            if (planSummary.Delete > 0)
                results.OperationProgressUpdater.UpdatefilesProcessed(planSummary.Upload + planSummary.Update + planSummary.UnchangedFiles + planSummary.Delete, planSummary.SizeOfUploadedFiles + planSummary.SizeOfDeletedFiles);

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
    /// <param name="db">The sync database.</param>
    /// <param name="backendManager">The backend manager used to perform the upload.</param>
    /// <param name="results">The sync results whose progress updater receives per-file progress.</param>
    /// <param name="relPath">The relative path on the remote destination.</param>
    /// <param name="lf">The local source entry to upload.</param>
    /// <param name="operation">The intent operation to record (<see cref="SyncOperation.Upload"/> or <see cref="SyncOperation.Update"/>).</param>
    /// <param name="logLabel">The label used in the log message (e.g. "Uploading" or "Updating").</param>
    /// <param name="verifyHash">Whether to compute and store a content hash.</param>
    /// <param name="maintainInventory">Whether to write the observed state back into the inventory cache (UseLocalState). Under UseRemoteState/BlindlyUpload the inventory is not maintained.</param>
    /// <param name="isDryRun">Whether this is a dry run; when true the upload is logged but not performed.</param>
    /// <param name="planSummary">The running plan counters to bump for the summary log.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    private static async Task UploadOrUpdateAsync(
        LocalSyncDatabase db,
        IBackendManager backendManager,
        SyncResults results,
        string relPath,
        ISourceProviderEntry lf,
        SyncOperation operation,
        string logLabel,
        bool verifyHash,
        bool maintainInventory,
        bool isDryRun,
        PlanSummary planSummary,
        CancellationToken cancellationToken)
    {
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
        results.OperationProgressUpdater.StartFile(relPath, Math.Max(lf.Size, 0));

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
        await backendManager.PutFileUnencryptedAsync(relPath, temp, cancellationToken);

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
        results.OperationProgressUpdater.UpdatefilesProcessed(planSummary.Upload + planSummary.Update + planSummary.UnchangedFiles, planSummary.SizeOfUploadedFiles);
    }

    /// <summary>
    /// Computes the relative root path on the remote destination for a source root,
    /// matching the relative-path convention used throughout the handler. For a
    /// single source the source's contents map directly onto the backend root
    /// (relative root ""); for multiple sources each source contributes its own name
    /// as a top-level sub-folder.
    /// </summary>
    private static string GetSourceRelativeRoot(string sourcePath, string[] sources)
    {
        if (sources.Length == 1)
            return "";

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
    private sealed record FolderWorkItem(ISourceProviderEntry Entry, string RelativePath);

    /// <summary>
    /// A remote child entry (file) observed in a folder, as returned by a listing or
    /// read from the inventory cache. Keyed by name within the folder.
    /// </summary>
    private sealed record RemoteChild(string Name, long Size, DateTime LastModified, string? ContentHash);

    /// <summary>
    /// Running counters for the plan summary log, bumped per operation as it is
    /// planned/performed so the final log line reflects the whole run. The same
    /// counters are flushed into <see cref="SyncResults"/> at the end of the run.
    /// </summary>
    private sealed class PlanSummary
    {
        public long Upload;
        public long Update;
        public long Delete;
        public long FoldersCreated;
        public long UnchangedFiles;
        public long SourceFiles;
        public long SizeOfSourceFiles;
        public long SizeOfUploadedFiles;
        public long SizeOfDeletedFiles;
    }

    /// <summary>
    /// Intermediate record holding a manager and the associated database
    /// </summary>
    /// <param name="BackendManager">The backend manager</param>
    /// <param name="Database">The database for the manager</param>
    private sealed record ManagerInstance(
        IBackendManager BackendManager,
        LocalSyncDatabase Database
    );

    /// <summary>
    /// Per-backend run state for the shared single-enumeration sync: the backend
    /// manager + database pair, the resolved <see cref="SyncRemoteState"/> for this
    /// run (after the UseLocalState-fallback is applied), and whether the inventory
    /// cache is maintained for this backend. Computed once per backend before the
    /// shared folder loop so the per-folder fan-out applies the same observed local
    /// state to every backend without re-resolving (or re-enumerating) per backend.
    /// </summary>
    private sealed record BackendRunState(
        IBackendManager BackendManager,
        LocalSyncDatabase Database,
        SyncRemoteState StateMode,
        bool MaintainInventory
    );
}