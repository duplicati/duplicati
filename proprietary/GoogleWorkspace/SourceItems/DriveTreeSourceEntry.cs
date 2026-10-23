// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.SourceProvider.ChangeTracking;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using System.Runtime.CompilerServices;
using File = Google.Apis.Drive.v3.Data.File;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// The folder at the top of a drive: a user's My Drive, or the content of a shared drive. It
/// has the path and metadata of the folder it stands for, and when the provider tracks changes
/// and a change token is stored for the drive, only the items created, changed, moved or
/// removed since the previous backup are listed, from a few paged requests to the changes feed
/// instead of a request per folder, and the rest is carried forward by the backup.
/// </summary>
/// <param name="provider">The provider</param>
/// <param name="parentPath">The path of the parent</param>
/// <param name="userId">The user the drive is listed through, or <c>null</c> at the tenant level</param>
/// <param name="userIsInactive">Whether the user is suspended or archived</param>
/// <param name="name">The folder name</param>
/// <param name="folderId">The id the folder is listed by: the <c>root</c> alias for My Drive, the drive id for a shared drive</param>
/// <param name="driveId">The shared drive id, or <c>null</c> for My Drive</param>
/// <param name="driveService">The Drive service</param>
internal class DriveTreeSourceEntry(SourceProvider provider, string parentPath, string? userId, bool userIsInactive, string name, string folderId, string? driveId, DriveService driveService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, folderId)), null, null)
{
    private static readonly string LOGTAG = Log.LogTagFromType<DriveTreeSourceEntry>();

    /// <summary>
    /// The largest page the changes feed allows
    /// </summary>
    private const int CHANGES_PAGE_SIZE = 1000;

    /// <summary>
    /// The fields requested from the changes feed
    /// </summary>
    private const string CHANGE_FIELDS = "nextPageToken, newStartPageToken, changes(fileId, removed, changeType, driveId, file(" + DriveFolderSourceEntry.FILE_FIELDS + "))";

    /// <summary>
    /// The outcome of reading the changes: the changes arranged for listing and the token to
    /// continue from, or nothing when the drive has to be listed in full
    /// </summary>
    /// <param name="Changes">The changes</param>
    /// <param name="Roots">The changed items whose parent is unchanged, with their parent's path</param>
    /// <param name="NewToken">The start page token of the next read</param>
    private sealed record IncrementalPlan(DriveChangeSet Changes, List<(File File, string ParentPath)> Roots, string NewToken);

    /// <summary>
    /// The name the drive is reported by in log messages
    /// </summary>
    private string DriveName => driveId == null ? $"My Drive of {userId}" : (userId == null ? driveId : $"{driveId} (through {userId})");

    /// <summary>
    /// The change tracking key of the drive
    /// </summary>
    private string StateKey => driveId == null
        ? ChangeTrackingKeys.MyDrive(userId ?? throw new InvalidOperationException("My Drive is listed without a user"))
        : ChangeTrackingKeys.SharedDrive(userId, driveId);

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!provider.DriveDeltaActive)
        {
            await foreach (var entry in EnumerateChildrenAsync(null, null, cancellationToken).ConfigureAwait(false))
                yield return entry;
            yield break;
        }

        var tracker = provider.ChangeTracker;
        var stateKey = StateKey;
        var storedValue = tracker.GetState(stateKey);
        var storedMap = storedValue == null ? null : FolderMap.Deserialize(storedValue);
        if (storedValue != null && storedMap == null)
        {
            Log.WriteWarningMessage(LOGTAG, "DriveStateUnreadable", null, Strings.DriveStateUnreadable(DriveName));
            tracker.MarkLoadedStateInvalid(stateKey);
        }

        var completed = false;
        try
        {
            if (storedMap != null)
            {
                // Decide what the carry-forward sees before anything can fail: the drive is
                // walked incrementally and unreported paths below it are copied from the
                // previous backup. A failed read then carries everything forward unchanged
                tracker.MarkIncremental(Path);

                var plan = await TryReadChangesAsync(storedMap, stateKey, cancellationToken).ConfigureAwait(false);
                if (plan != null)
                {
                    // The map keeps growing while the backup recurses into moved folders, so
                    // it is serialized when the state is exported, not now
                    var map = plan.Changes.Map;
                    map.Token = plan.NewToken;
                    tracker.SetState(stateKey, () => map.Serialize());

                    foreach (var (file, parentPath) in plan.Roots)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            yield break;

                        yield return plan.Changes.CreateEntry(provider, stateKey, userId, userIsInactive, driveService, file, parentPath);
                    }

                    completed = true;
                    yield break;
                }

                // The changes could not be used; everything below the drive is listed now
                tracker.MarkExaminedSubtree(Path);
            }

            // Take the token before the walk, so anything that changes during the walk is
            // reported by the next read; a change reported twice is harmless, a missed one is not
            var startToken = await GetStartPageTokenAsync(cancellationToken).ConfigureAwait(false);
            var rootId = await GetRootIdAsync(cancellationToken).ConfigureAwait(false);
            var freshMap = new FolderMap { Token = startToken, RootId = rootId };

            // The folders below the top level are only found when the backup recurses into
            // them, after this listing has ended, so the map is serialized when the state is exported
            tracker.SetState(stateKey, () => freshMap.Serialize());

            await foreach (var entry in EnumerateChildrenAsync(freshMap, stateKey, cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return entry;
            }

            completed = true;
        }
        finally
        {
            // A walk that failed keeps the previous token and map, so the drive is carried
            // forward unchanged and the changes are reported again next time; after repeated
            // failures the tracker resets the state so the drive is walked in full. A first
            // walk that failed has nothing to keep, and its token is not stored so the next
            // run walks the drive in full instead of continuing from a token taken before an
            // incomplete listing
            if (!completed)
                tracker.MarkStateFailed(stateKey);
        }
    }

    /// <summary>
    /// Lists the top-level items with a request to Drive, recording the folders in the map
    /// </summary>
    /// <param name="map">The folder map to record folders in, or <c>null</c> if changes are not tracked</param>
    /// <param name="stateKey">The change tracking key of the drive, or <c>null</c> if changes are not tracked</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entries</returns>
    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateChildrenAsync(FolderMap? map, string? stateKey, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var children = provider.ChangeTracker.ListWithFailureTrackingAsync(stateKey, DriveFolderSourceEntry.ListChildrenAsync(driveService, folderId, cancellationToken), cancellationToken);
        await foreach (var file in children.ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (GoogleMimeTypes.IsFolder(file.MimeType))
            {
                if (map?.RootId != null)
                    map.SetParent(file.Id, map.RootId);
                yield return new DriveFolderSourceEntry(provider, stateKey, Path, userId, userIsInactive, file.Name, file.Id, driveService, map);
            }
            else
            {
                yield return new DriveFileSourceEntry(provider, stateKey, Path, file, userIsInactive, driveService);
            }
        }
    }

    /// <summary>
    /// Gets the token the next read of the changes feed starts from
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The start page token</returns>
    private async Task<string> GetStartPageTokenAsync(CancellationToken cancellationToken)
    {
        var request = driveService.Changes.GetStartPageToken();
        request.SupportsAllDrives = true;
        if (driveId != null)
            request.DriveId = driveId;

        var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(response.StartPageTokenValue)
            ? throw new UserInformationException(Strings.DriveChangesTokenMissing(DriveName), "DriveStartPageTokenMissing")
            : response.StartPageTokenValue;
    }

    /// <summary>
    /// Gets the id the changes feed reports as the parent of the top-level items. A shared
    /// drive's root is the drive id; My Drive is listed by the <c>root</c> alias, which the
    /// feed does not use, so its real id is looked up
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The root id</returns>
    private async Task<string> GetRootIdAsync(CancellationToken cancellationToken)
    {
        if (driveId != null)
            return driveId;

        var request = driveService.Files.Get(folderId);
        request.Fields = "id";
        var root = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(root.Id)
            ? throw new UserInformationException($"The root folder of {DriveName} has no id", "DriveRootWithoutId")
            : root.Id;
    }

    /// <summary>
    /// Reads the changes feed from the stored token and arranges the changes for listing
    /// </summary>
    /// <param name="map">The stored folder map with its token, updated with the changes</param>
    /// <param name="stateKey">The change tracking key of the drive</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The plan, or <c>null</c> if the drive must be listed in full</returns>
    private async Task<IncrementalPlan?> TryReadChangesAsync(FolderMap map, string stateKey, CancellationToken cancellationToken)
    {
        var tracker = provider.ChangeTracker;
        var pageToken = map.Token ?? throw new UserInformationException($"The stored change tracking state of {DriveName} has no token", "DriveStateWithoutToken");

        // The feed is flat and may report an item on several pages; the last report is the newest state
        var items = new Dictionary<string, Change>(StringComparer.Ordinal);
        string? newToken = null;
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = driveService.Changes.List(pageToken);
                request.PageSize = CHANGES_PAGE_SIZE;
                request.IncludeRemoved = true;
                request.Spaces = "drive";
                request.SupportsAllDrives = true;
                request.Fields = CHANGE_FIELDS;
                if (driveId != null)
                {
                    request.DriveId = driveId;
                    request.IncludeItemsFromAllDrives = true;
                }
                else
                {
                    request.RestrictToMyDrive = true;
                }

                var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                foreach (var change in response.Changes ?? [])
                {
                    // A change to the shared drive itself, such as a rename, is not an item
                    if (string.IsNullOrEmpty(change.FileId) || string.Equals(change.ChangeType, "drive", StringComparison.OrdinalIgnoreCase))
                        continue;

                    items[change.FileId] = change;
                    if (items.Count > provider.Options.DriveDeltaMaxItems)
                    {
                        Log.WriteInformationMessage(LOGTAG, "DriveChangesTooMany", Strings.DriveChangesTooMany(DriveName, provider.Options.DriveDeltaMaxItems));
                        tracker.MarkLoadedStateInvalid(stateKey);
                        return null;
                    }
                }

                if (!string.IsNullOrWhiteSpace(response.NewStartPageToken))
                    newToken = response.NewStartPageToken;
                pageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (Exception ex) when (GoogleServiceErrors.IsDriveChangesTokenRejected(ex))
        {
            Log.WriteInformationMessage(LOGTAG, "DriveChangesTokenRejected", Strings.DriveChangesTokenRejected(DriveName, (int)((Google.GoogleApiException)ex).HttpStatusCode));
            tracker.MarkLoadedStateInvalid(stateKey);
            return null;
        }

        if (newToken == null)
            throw new UserInformationException(Strings.DriveChangesTokenMissing(DriveName), "DriveChangesTokenMissing");

        // Arranging the changes may ask Drive for the folders the map does not know. A failure
        // there, or an inconsistent map, leaves the drive without a usable plan, and listing it in
        // full now is better than failing the drive and carrying it forward unchanged for several
        // backups until the tracker resets the state
        try
        {
            return await ArrangeChangesAsync(items, map, newToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.WriteWarningMessage(LOGTAG, "DriveChangesArrangeFailed", ex, Strings.DriveChangesArrangeFailed(DriveName));
            tracker.MarkLoadedStateInvalid(stateKey);
            return null;
        }
    }

    /// <summary>
    /// Whether a reported change means the item is no longer part of this drive: it was
    /// removed, access to it was lost, it was trashed, or it now lives in another drive
    /// </summary>
    /// <param name="change">The change</param>
    /// <returns><c>true</c> if the item is gone from the drive</returns>
    private bool IsRemoved(Change change)
        => change.Removed == true
            || change.File == null
            || change.File.Trashed == true
            || (driveId == null && !string.IsNullOrEmpty(change.File.DriveId))
            || (driveId != null && !string.IsNullOrEmpty(change.File.DriveId) && !string.Equals(change.File.DriveId, driveId, StringComparison.Ordinal));

    /// <summary>
    /// Picks the parent an item is listed under. Drive files have had a single parent since
    /// 2020; a legacy file with several is listed under the one the map knows, so it keeps
    /// the path it had, or under the first one
    /// </summary>
    /// <param name="file">The item</param>
    /// <param name="map">The folder map</param>
    /// <returns>The parent id, or <c>null</c> if the item has none</returns>
    private static string? PickParent(File file, FolderMap map)
    {
        var parents = (file.Parents ?? []).Where(x => !string.IsNullOrEmpty(x)).ToList();
        if (parents.Count == 0)
            return null;
        if (parents.Count == 1)
            return parents[0];

        return parents.FirstOrDefault(p => p == map.RootId || map.Contains(p)) ?? parents[0];
    }

    /// <summary>
    /// Arranges the items the changes feed reported for listing: records what the carry-forward
    /// must not copy, updates the folder map and resolves the path of every changed item
    /// </summary>
    /// <param name="items">The reported changes by file id</param>
    /// <param name="map">The stored folder map, updated with the changes</param>
    /// <param name="newToken">The token the read ended with</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The plan</returns>
    private async Task<IncrementalPlan> ArrangeChangesAsync(Dictionary<string, Change> items, FolderMap map, string newToken, CancellationToken cancellationToken)
    {
        var tracker = provider.ChangeTracker;
        var rootId = map.RootId ?? throw new UserInformationException($"The stored folder map of {DriveName} has no root", "DriveFolderMapWithoutRoot");
        var changes = new DriveChangeSet(map);
        var reported = items.Values.Where(x => x.FileId != rootId).ToList();
        var live = reported.Where(x => !IsRemoved(x)).Select(x => x.File!).ToList();

        // Removed items first: a removed folder takes its whole subtree with it, and the feed
        // does not list the children of a removed folder. Ids are unique, so any previous path
        // with the id as a segment is at or below the item
        foreach (var change in reported.Where(IsRemoved))
        {
            tracker.MarkIdSubtreeExamined(Path, change.FileId);
            map.Remove(change.FileId);
        }

        // Then folders: a folder whose parent differs from the stored one was moved, and its
        // old path, which has its id as a segment, is gone. A folder the map does not know is
        // treated the same way: it is either new, in which case its old path does not exist
        // and the walk of its contents finds what the feed reported anyway, or it was moved
        // while absent from the map, which must not leave its old path behind. A folder that
        // stays in place only re-lists itself, and its contents are carried forward
        foreach (var file in live.Where(x => GoogleMimeTypes.IsFolder(x.MimeType)))
        {
            tracker.MarkIdTouched(Path, file.Id);
            var parentId = PickParent(file, map);
            if (parentId == null)
            {
                Log.WriteVerboseMessage(LOGTAG, "DriveChangeWithoutParent", "The changes of {0} report folder {1} without a parent, skipping it", DriveName, file.Id);
                continue;
            }

            if (!map.TryGetParent(file.Id, out var previousParent) || !string.Equals(previousParent, parentId, StringComparison.Ordinal))
            {
                changes.MovedFolders.Add(file.Id);
                tracker.MarkIdSubtreeExamined(Path, file.Id);
            }

            map.SetParent(file.Id, parentId);
        }

        // A changed file is a folder of derived entries, all re-listed at its current path; if
        // it was moved, its old path has its id as a segment and is dropped
        foreach (var file in live.Where(x => !GoogleMimeTypes.IsFolder(x.MimeType)))
            tracker.MarkIdSubtreeExamined(Path, file.Id);

        var pruned = map.PruneUnreachable();
        if (pruned > 0)
            Log.WriteVerboseMessage(LOGTAG, "DriveFolderMapPruned", "Dropped {0} folders below removed folders from the folder map of {1}", pruned, DriveName);

        // Arrange the changes as a tree: an item below a changed folder is listed by that folder,
        // an item below a moved folder is listed by the walk of the moved folder, and the rest
        // are listed at their resolved path. An item whose ancestors do not reach the root left
        // the drive and is dropped
        var folderPaths = new Dictionary<string, string>(StringComparer.Ordinal) { [rootId] = Path };
        var crossesMoved = new Dictionary<string, bool>(StringComparer.Ordinal);
        var roots = new List<(File File, string ParentPath)>();
        var left = 0;
        foreach (var file in live)
        {
            var parentId = PickParent(file, map);
            if (parentId == null)
                continue;

            if (await ChainCrossesMovedFolderAsync(parentId, map, changes.MovedFolders, crossesMoved, cancellationToken).ConfigureAwait(false))
                continue;

            if (items.TryGetValue(parentId, out var parent) && !IsRemoved(parent) && GoogleMimeTypes.IsFolder(parent.File!.MimeType) && parentId != rootId)
            {
                changes.Add(parentId, file);
                continue;
            }

            var parentPath = await ResolveFolderPathAsync(parentId, map, folderPaths, cancellationToken).ConfigureAwait(false);
            if (parentPath == null)
            {
                left++;
                map.Remove(file.Id);
                continue;
            }

            roots.Add((file, parentPath));
        }

        Log.WriteVerboseMessage(LOGTAG, "DriveChangesRead", "The changes of {0} reported {1} items, of which {2} are listed at the top level, {3} folders were moved and {4} items left the drive", DriveName, reported.Count, roots.Count, changes.MovedFolders.Count, left);
        return new IncrementalPlan(changes, roots, newToken);
    }

    /// <summary>
    /// Whether a folder or one of its ancestors was moved, in which case everything below
    /// it is listed by the walk of the moved folder. The answer for every folder on the
    /// chain is cached, so a chain is walked once however many items share it.
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <param name="map">The folder map</param>
    /// <param name="movedFolders">The ids of the moved folders</param>
    /// <param name="cache">The answers found so far</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns><c>true</c> if the folder is at or below a moved folder</returns>
    private async Task<bool> ChainCrossesMovedFolderAsync(string folderId, FolderMap map, HashSet<string> movedFolders, Dictionary<string, bool> cache, CancellationToken cancellationToken)
    {
        if (movedFolders.Count == 0)
            return false;

        var chain = new List<string>();
        var current = folderId;
        var result = false;
        while (current != null && current != map.RootId && chain.Count < 1024)
        {
            if (cache.TryGetValue(current, out var known))
            {
                result = known;
                break;
            }

            if (movedFolders.Contains(current))
            {
                result = true;
                break;
            }

            chain.Add(current);
            current = await GetParentAsync(current, map, cancellationToken).ConfigureAwait(false);
        }

        foreach (var id in chain)
            cache[id] = result;

        return result;
    }

    /// <summary>
    /// Resolves the entry path of a folder from the chain of its ancestors
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <param name="map">The folder map</param>
    /// <param name="cache">The paths resolved so far</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The folder path, ending with the directory separator, or <c>null</c> if the chain leaves the drive</returns>
    private async Task<string?> ResolveFolderPathAsync(string folderId, FolderMap map, Dictionary<string, string> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(folderId, out var cached))
            return cached;

        // Walk up to the first known path, then build the paths back down
        var chain = new List<string>();
        var current = folderId;
        while (!cache.TryGetValue(current, out cached))
        {
            if (chain.Count > 1024 || chain.Contains(current))
                throw new UserInformationException($"The folder map of {DriveName} has a cycle at folder {current}", "DriveFolderMapCycle");

            chain.Add(current);
            var parent = await GetParentAsync(current, map, cancellationToken).ConfigureAwait(false);
            if (parent == null)
            {
                Log.WriteVerboseMessage(LOGTAG, "DriveChangeOutsideTree", "Folder {0} of {1} is not below the root of the drive, dropping what is below it", current, DriveName);
                foreach (var id in chain)
                    map.Remove(id);
                return null;
            }

            current = parent;
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            cached = Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(cached, chain[i]));
            cache[chain[i]] = cached;
        }

        return cached;
    }

    /// <summary>
    /// Gets the parent of a folder from the map, asking Drive for a folder the map does not
    /// know, which happens when the folder was not walked in a previous backup
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <param name="map">The folder map</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The parent id, or <c>null</c> if the folder is not below the root of this drive</returns>
    private async Task<string?> GetParentAsync(string folderId, FolderMap map, CancellationToken cancellationToken)
    {
        if (map.TryGetParent(folderId, out var parentId))
            return parentId;

        Log.WriteVerboseMessage(LOGTAG, "DriveFolderMapMiss", "Folder {0} of {1} is not in the stored folder map, looking it up", folderId, DriveName);
        var request = driveService.Files.Get(folderId);
        request.Fields = "id, parents, driveId, trashed";
        request.SupportsAllDrives = true;
        var folder = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        // A folder without a parent is the root of something else, and a folder in another
        // drive, or in the trash, is not below this drive's root
        if (folder.Trashed == true
            || (driveId == null && !string.IsNullOrEmpty(folder.DriveId))
            || (driveId != null && !string.IsNullOrEmpty(folder.DriveId) && !string.Equals(folder.DriveId, driveId, StringComparison.Ordinal)))
            return null;

        parentId = PickParent(folder, map);
        if (parentId == null)
            return null;

        map.SetParent(folderId, parentId);
        return parentId;
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFolder.ToString() },
            { "gsuite:Name", name },
            { "gsuite:Id", folderId }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
