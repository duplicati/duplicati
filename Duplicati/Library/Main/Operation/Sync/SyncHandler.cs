using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database.Sync;
using Duplicati.Library.Utility;
using Duplicati.Library.Main.Operation.Backup;
using Duplicati.Library.Main.Operation.Common;

#nullable enable

namespace Duplicati.Library.Main.Operation.Sync;

internal class SyncHandler
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<SyncHandler>();
    private readonly Options m_options;
    private readonly SyncResults m_results;
    private readonly string[] m_sources;

    public SyncHandler(string[] sources, Options options, SyncResults results)
    {
        m_sources = sources;
        m_options = options;
        m_results = results;
    }

    public async Task RunAsync(IBackendManager backendManager, IFilter filter)
    {
        var ct = m_results.TaskControl.ProgressToken;
        Logging.Log.WriteInformationMessage(LOGTAG, "SyncStarted", "Starting sync");

        var dbPath = m_options.Dbpath
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
        var stateMode = (configuredStateMode == SyncRemoteState.UseLocalState && m_options.SyncRecheck)
            ? SyncRemoteState.UseRemoteState
            : configuredStateMode;

        // BlindlyUpload has no remote state to diff against, so deleting remote files
        // is not meaningful. Warn once at the start so the user is not surprised that
        // --sync-then-delete is ignored for the run.
        if (stateMode == SyncRemoteState.BlindlyUpload && isDelete)
            Logging.Log.WriteWarningMessage(LOGTAG, "BlindlyUploadIgnoresDelete", null, $"sync-remote-state={SyncRemoteState.BlindlyUpload} cannot determine which remote files to delete; --sync-then-delete will be ignored for this run.");

        // Under BlindlyUpload we never read or write the inventory cache, so the
        // observed-state table is left untouched. Under UseRemoteState we list each
        // folder fresh and do not use the inventory as the diff baseline (the remote is
        // authoritative per folder) - but we still write-through the inventory when
        // --sync-verify-hash is set, so the content hashes computed during uploads are
        // remembered and can drive hash re-checks on later runs (a listing carries no
        // hash). Under UseLocalState the inventory is both the diff baseline and kept
        // write-through as we upload/delete.
        using var db = new LocalSyncDatabase(dbPath);

        // Resume: leftover PendingOperation rows mean a previous run was interrupted
        // mid-operation. Under UseRemoteState each folder is listed fresh this run, so
        // any in-flight intent is naturally superseded by the live remote state and the
        // leftover rows can simply be cleared. Under UseLocalState the inventory may be
        // stale for the interrupted paths; we clear the rows but warn the user that a
        // recheck (UseRemoteState) is the way to reconcile fully. Under BlindlyUpload
        // the journal is never consulted, so leftover rows are just noise; clear them.
        if (await db.HasAnyPendingOperationsAsync(ct).ConfigureAwait(false))
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "InflightDetected", null, "Detected incomplete operations from a previous run. Clearing the intent journal; {0}.", stateMode == SyncRemoteState.UseLocalState ? "run with --sync-recheck (or sync-remote-state=use-remote-state) to reconcile against a fresh remote listing" : "the current run will re-establish remote state per folder");
            await db.ClearPendingOperationsAsync(ct).ConfigureAwait(false);
        }

        // If UseLocalState is configured but the inventory is empty (e.g. first run, or
        // the database was deleted), there is no baseline to diff against. Fall back to
        // UseRemoteState for this run so each folder is listed fresh; the inventory is
        // populated as we go, so the next UseLocalState run has a baseline. This keeps
        // the first run correct without forcing the user to pass a flag.
        var forcePopulateInventory = false;
        if (stateMode == SyncRemoteState.UseLocalState && !await db.HasAnyInventoryAsync(ct).ConfigureAwait(false))
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

        using var sourceProvider = await SourceProviderFactory.GetSourceProviderAsync(m_sources, m_options, ct).ConfigureAwait(false);

        var blacklistPaths = BackupHandler.GetBlacklistedPaths(m_options);
        var fileAttributeFilter = m_options.FileAttributeFilter;
        var symlinkPolicy = m_options.SymlinkPolicy;
        var hardlinkPolicy = m_options.HardlinkPolicy;
        var disableBackupExclusionXattr = m_options.DisableBackupExclusionXattr;
        var excludeEmptyFolders = m_options.ExcludeEmptyFolders;
        var ignoreNames = m_options.IgnoreFilenames;

        // Seed the folder queue with one entry per source root. The relative path of a
        // source root follows the relative-path convention used throughout the handler:
        // for a single source the source's contents map directly onto the backend root
        // (relative root ""); for multiple sources each source contributes its own name
        // as a top-level sub-folder on the remote. The local folder entry for each
        // queued root is the source root itself, resolved through the source provider.
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

        var planSummary = new PlanSummary();

        while (folderQueue.Count > 0)
        {
            if (ct.IsCancellationRequested)
                break;

            var current = folderQueue.Dequeue();
            await ProcessFolderAsync(
                current,
                sourceProvider,
                backendManager,
                db,
                filter,
                fileAttributeFilter,
                symlinkPolicy,
                hardlinkPolicy,
                disableBackupExclusionXattr,
                excludeEmptyFolders,
                ignoreNames,
                blacklistPaths,
                stateMode,
                maintainInventory,
                isDryRun,
                isDelete,
                verifyHash,
                planSummary,
                folderQueue,
                ct).ConfigureAwait(false);
        }

        Logging.Log.WriteInformationMessage(LOGTAG, "SyncPlan", "Plan: Upload {0}, Update {1}, Delete {2}", planSummary.Upload, planSummary.Update, planSummary.Delete);
        Logging.Log.WriteInformationMessage(LOGTAG, "SyncFinished", "Sync completed successfully.");
    }

    /// <summary>
    /// Processes a single folder: enumerates its local children, checks the remote
    /// state of the folder, creates any missing sub-folders, uploads new/changed
    /// files, and (optionally) deletes unknown remote files. Sub-folders are enqueued
    /// for later processing. This is the per-folder unit of work that keeps the
    /// memory footprint bounded: only one folder's children are resident at a time.
    /// </summary>
    private static async Task ProcessFolderAsync(
        FolderWorkItem current,
        ISourceProvider sourceProvider,
        IBackendManager backendManager,
        LocalSyncDatabase db,
        IFilter filter,
        FileAttributes fileAttributeFilter,
        Options.SymlinkStrategy symlinkPolicy,
        Options.HardlinkStrategy hardlinkPolicy,
        bool disableBackupExclusionXattr,
        bool excludeEmptyFolders,
        string[]? ignoreNames,
        HashSet<string> blacklistPaths,
        SyncRemoteState stateMode,
        bool maintainInventory,
        bool isDryRun,
        bool isDelete,
        bool verifyHash,
        PlanSummary planSummary,
        Queue<FolderWorkItem> folderQueue,
        CancellationToken ct)
    {
        var folderRelPath = current.RelativePath;
        var folderEntry = current.Entry;

        // 1+2. Enumerate the local folder's direct children (non-recursive) and split
        // them into sub-folders (to enqueue) and files (to upload this pass). The
        // filter reuses the backup enumeration filter so attribute/symlink/hardlink/
        // blacklist/xattr/ignore-name behavior is identical between backup and sync.
        // Only this folder's children are resident in memory at once.
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
                // INSERT OR IGNORE semantics: the first entry seen for a path wins, so a
                // later duplicate in the same folder is ignored (paths are unique within
                // a single folder in practice).
                if (!localFiles.ContainsKey(childRelPath))
                    localFiles[childRelPath] = (child, child.Size, child.LastModificationUtc);
            }
        }

        // 3. Check remote state for the current folder. Under UseRemoteState we list
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

        // 4. Create any sub-folders that are missing on the remote. Under UseRemoteState
        // we know exactly which sub-folders already exist (from the listing); under
        // UseLocalState we treat a sub-folder as existing if the inventory has any file
        // under it, otherwise we ensure it; under BlindlyUpload we ensure every
        // sub-folder unconditionally (the root is also ensured below). Ensuring a
        // folder that already exists is a no-op on the backend (CreateFolder treats
        // FolderAlreadyExisted as success) but still a network call, so we avoid it
        // when we have positive evidence the folder exists.
        foreach (var (subEntry, subRelPath) in localSubfolders)
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
                    await backendManager.EnsureFolderAsync(subRelPath, ct).ConfigureAwait(false);
            }

            // ExcludeEmptyFolders: skip enqueueing a sub-folder that has no local
            // children. We can't know that without enumerating it, so we defer the
            // check to when we process it: an empty folder simply contributes no
            // files and no further sub-folders. To match the backup behavior of not
            // creating empty folders at all, we drop the queued entry here if it
            // would be empty. Since enumerating now defeats the bounded-memory goal,
            // we instead let it be processed and rely on the remote folder having
            // been created only if it had at least one file. To honor the option
            // precisely we do a quick peek: enumerate the sub-folder's direct
            // children and only queue/enforce-create if non-empty. This is one extra
            // enumeration per sub-folder but keeps memory bounded and matches the
            // backup semantics. The peek is skipped under BlindlyUpload (we always
            // create) unless excludeEmptyFolders is set.
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

            folderQueue.Enqueue(new FolderWorkItem(subEntry, subRelPath));
        }

        // Under BlindlyUpload the current folder must also be ensured to exist before
        // we put files into it (we have no listing to confirm it). For UseRemoteState
        // and UseLocalState the folder's existence is implied: under UseRemoteState we
        // just listed it (so it exists); under UseLocalState we assume the inventory
        // is authoritative, and a folder containing files we're about to upload-into
        // either already has files (so it exists) or is brand new, in which case the
        // sub-folder ensure above created it when it was enqueued as a child. The
        // backend root (folderRelPath == "") is assumed to exist for all modes.
        if (stateMode == SyncRemoteState.BlindlyUpload && !string.IsNullOrEmpty(folderRelPath) && !isDryRun)
            await backendManager.EnsureFolderAsync(folderRelPath, ct).ConfigureAwait(false);

        // 5. Upload all new or changed files in this folder. The caller guarantees the
        // current folder exists, so UploadOrUpdateAsync does not call EnsureFolderAsync.
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
                continue;

            if (hashRecheck)
            {
                try
                {
                    using (var s = await entry.OpenRead(ct).ConfigureAwait(false))
                    using (var hasher = HashFactory.CreateHasher("SHA256"))
                    {
                        var hash = Convert.ToBase64String(hasher.ComputeHash(s));
                        if (hash == remoteHash)
                            continue; // Content unchanged; no upload needed.
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
                await UploadOrUpdateAsync(db, backendManager, relPath, entry, operation, label, verifyHash, maintainInventory, isDryRun, planSummary, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "UploadFailed", ex, "Failed to {0} file: {1}", label, relPath);
            }
        }

        // 6. (Optionally) delete unknown remote files in this folder. A remote file is
        // "unknown" if no local file with the same name exists in this folder. Under
        // BlindlyUpload we have no remote state, so deletes are skipped (the warning
        // was logged at the start of the run). Pending-operation intent rows are not
        // relevant here since the journal was cleared at the start of the run; a
        // delete cannot race its own upload because the upload for this folder
        // completed above before we delete.
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
            }

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
            return;
        }

        Logging.Log.WriteInformationMessage(LOGTAG, logLabel, "{0} {1}", logLabel, relPath);

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
    }

    /// <summary>
    /// Computes the relative root path on the remote destination for a source root,
    /// matching the relative-path convention used throughout the sync handler. For a
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
    /// planned/performed so the final log line reflects the whole run.
    /// </summary>
    private sealed class PlanSummary
    {
        public long Upload;
        public long Update;
        public long Delete;
    }
}
