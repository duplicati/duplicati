// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

/// <summary>
/// A drive, that is a SharePoint document library or a group's files. When the provider
/// tracks changes and a delta link is stored for the drive, only the items created,
/// changed, moved or removed since the previous backup are listed, from a few paged delta
/// requests instead of a request per folder, and the rest is carried forward by the backup.
/// </summary>
internal class DriveSourceEntry(SourceProvider provider, string path, GraphDrive drive)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, drive.Id)), drive.CreatedDateTime.FromGraphDateTime(), drive.LastModifiedDateTime.FromGraphDateTime())
{
    private static readonly string LOGTAG = Log.LogTagFromType<DriveSourceEntry>();

    /// <summary>
    /// The outcome of reading a drive delta: the changes arranged for listing and the link
    /// to continue from, or nothing when the drive has to be listed in full
    /// </summary>
    /// <param name="Changes">The changes</param>
    /// <param name="Roots">The changed items whose parent is unchanged, with their parent's path</param>
    /// <param name="DeltaToken">The token of the link the walk ended with</param>
    private sealed record IncrementalPlan(DriveChangeSet Changes, List<(GraphDriveItem Item, string ParentPath)> Roots, string DeltaToken);

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tracker = provider.ChangeTracker;
        if (!provider.DriveDeltaEnabled || !tracker.IsActive)
        {
            await foreach (var entry in EnumerateChildrenAsync(null, cancellationToken).ConfigureAwait(false))
                yield return entry;
            yield break;
        }

        var stateKey = ChangeTrackingKeys.Drive(drive.Id);
        var storedValue = tracker.GetState(stateKey);
        var storedMap = storedValue == null ? null : DriveFolderMap.Deserialize(storedValue);
        if (storedValue != null && storedMap == null)
            Log.WriteWarningMessage(LOGTAG, "DriveStateUnreadable", null, "The stored delta state for drive {0} cannot be read, listing the drive in full", drive.Id);

        var completed = false;
        try
        {
            if (storedMap != null)
            {
                // Decide what the carry-forward sees before anything can fail: the drive is
                // walked incrementally and unreported paths below it are copied from the
                // previous backup. A failed delta then carries everything forward unchanged.
                tracker.MarkIncremental(Path);

                var plan = await TryReadDeltaAsync(storedMap, cancellationToken).ConfigureAwait(false);
                if (plan != null)
                {
                    // The map keeps growing while the backup recurses into moved folders, so
                    // it is serialized when the state is exported, not now
                    var map = plan.Changes.Map;
                    map.Token = plan.DeltaToken;
                    tracker.SetState(stateKey, () => map.Serialize());

                    foreach (var (item, parentPath) in plan.Roots)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            yield break;

                        var entry = plan.Changes.CreateEntry(provider, drive, item, parentPath);
                        if (entry != null)
                            yield return entry;
                    }

                    completed = true;
                    yield break;
                }

                // The delta could not be used; everything below the drive is listed now
                tracker.MarkExaminedSubtree(Path);
            }

            // Take the token before the walk, so anything that changes during the walk is
            // reported by the next delta; a change reported twice is harmless, a missed one is not
            var latestToken = await provider.OneDriveApi.GetDriveDeltaLatestTokenAsync(drive.Id, cancellationToken).ConfigureAwait(false);
            var root = await provider.OneDriveApi.GetDriveRootAsync(drive.Id, cancellationToken).ConfigureAwait(false);
            var freshMap = new DriveFolderMap { Token = latestToken, RootId = root.Id };

            // The folders below the top level are only found when the backup recurses into
            // them, after this listing has ended, so the map is serialized when the state is exported
            tracker.SetState(stateKey, () => freshMap.Serialize());

            await foreach (var entry in EnumerateChildrenAsync(freshMap, cancellationToken).ConfigureAwait(false))
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
            // forward unchanged and the changes are reported again next time. A first walk
            // that failed has nothing to keep, and its token is dropped so the next run walks
            // the drive in full instead of continuing from a token taken before an incomplete listing
            if (!completed)
                tracker.MarkStateFailed(stateKey);
        }
    }

    /// <summary>
    /// Lists the top-level items with a request to Graph, recording the folders in the map
    /// </summary>
    /// <param name="map">The folder map to record folders in, or <c>null</c> if changes are not tracked</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entries</returns>
    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateChildrenAsync(DriveFolderMap? map, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in provider.OneDriveApi.ListDriveRootChildrenAsync(drive.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (item.Deleted != null)
                continue;

            if (item.Folder != null)
            {
                if (map?.RootId != null)
                    map.SetParent(item.Id, map.RootId);
                yield return new DriveFolderSourceEntry(provider, Path, drive, item, map);
            }
            else if (item.File != null)
                yield return new DriveFileSourceEntry(provider, Path, drive, item);
            else
                Log.WriteWarningMessage(LOGTAG, "UnknownDriveItemType", null, $"Unknown drive item type: {item.Id}");
        }
    }

    /// <summary>
    /// Reads the delta from the stored link and arranges the changes for listing
    /// </summary>
    /// <param name="map">The stored folder map with its token, updated with the changes</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The plan, or <c>null</c> if the drive must be listed in full</returns>
    private async Task<IncrementalPlan?> TryReadDeltaAsync(DriveFolderMap map, CancellationToken cancellationToken)
    {
        var tracker = provider.ChangeTracker;
        var storedToken = map.Token ?? throw new UserInformationException($"The stored delta state for drive {drive.Id} has no token", "DriveStateWithoutToken");
        var deltaLink = new DeltaLinkHolder();

        // The response is flat and may report an item on several pages; the last one is the newest state
        var items = new Dictionary<string, GraphDriveItem>(StringComparer.Ordinal);
        try
        {
            await foreach (var item in provider.OneDriveApi.ListDriveDeltaAsync(drive.Id, storedToken, deltaLink, cancellationToken).ConfigureAwait(false))
            {
                items[item.Id] = item;
                if (items.Count > provider.DriveDeltaMaxItems)
                {
                    Log.WriteInformationMessage(LOGTAG, "DriveDeltaTooLarge", "Drive {0} reports more than {1} changes, listing the drive in full instead", drive.Id, provider.DriveDeltaMaxItems);
                    return null;
                }
            }
        }
        catch (Exception ex) when (APIHelper.IsDeltaResyncRequired(ex))
        {
            Log.WriteWarningMessage(LOGTAG, "DriveDeltaResync", ex, "The stored delta token for drive {0} is no longer valid, listing the drive in full", drive.Id);
            return null;
        }

        var newToken = DeltaToken.FromDriveDeltaLink(deltaLink.DeltaLink)
            ?? throw new UserInformationException($"The delta walk of drive {drive.Id} did not end with a delta link carrying a token: {deltaLink.DeltaLink}", "DriveDeltaTokenMissing");

        var rootId = map.RootId ?? throw new UserInformationException($"The stored folder map for drive {drive.Id} has no root", "DriveFolderMapWithoutRoot");
        var changes = new DriveChangeSet(map);
        var reported = items.Values.Where(x => x.Id != rootId).ToList();

        // Removed items first: a removed folder takes its whole subtree with it, and the delta
        // does not list the children of a removed folder. The map need not know the folder:
        // ids are unique, so any previous path with the id as a segment is below it
        foreach (var item in reported.Where(x => x.Deleted != null))
        {
            tracker.MarkIdTouched(item.Id);
            tracker.MarkIdSubtreeGone(item.Id);
            map.Remove(item.Id);
        }

        // Then folders: a folder whose parent differs from the stored one was moved, and its
        // old path, which has its id as a segment, is gone. A folder the map does not know is
        // treated the same way: it is either new, in which case its old path does not exist
        // and the walk of its contents finds what the delta reported anyway, or it was moved
        // while absent from the map, which must not leave its old path behind
        foreach (var item in reported.Where(x => x.Deleted == null && x.Folder != null))
        {
            tracker.MarkIdTouched(item.Id);
            var parentId = item.ParentReference?.Id;
            if (parentId == null)
            {
                Log.WriteVerboseMessage(LOGTAG, "DriveDeltaItemWithoutParent", "The delta of drive {0} reports folder {1} without a parent, skipping it", drive.Id, item.Id);
                continue;
            }

            if (!map.TryGetParent(item.Id, out var previousParent) || !string.Equals(previousParent, parentId, StringComparison.Ordinal))
            {
                changes.MovedFolders.Add(item.Id);
                tracker.MarkIdSubtreeGone(item.Id);
            }

            map.SetParent(item.Id, parentId);
        }

        // A changed file is listed at its current path; if it was moved, its old path has its id as the last segment
        foreach (var item in reported.Where(x => x.Deleted == null && x.Folder == null))
            tracker.MarkIdTouched(item.Id);

        var pruned = map.PruneUnreachable();
        if (pruned > 0)
            Log.WriteVerboseMessage(LOGTAG, "DriveFolderMapPruned", "Dropped {0} folders below removed folders from the folder map of drive {1}", pruned, drive.Id);

        // Arrange the changes as a tree: an item below a changed folder is listed by that folder,
        // an item below a moved folder is listed by the walk of the moved folder, and the rest
        // are listed at their resolved path
        var folderPaths = new Dictionary<string, string>(StringComparer.Ordinal) { [rootId] = Path };
        var roots = new List<(GraphDriveItem Item, string ParentPath)>();
        foreach (var item in reported.Where(x => x.Deleted == null))
        {
            var parentId = item.ParentReference?.Id;
            if (parentId == null)
                continue;

            if (await ChainCrossesMovedFolderAsync(parentId, map, changes.MovedFolders, cancellationToken).ConfigureAwait(false))
                continue;

            if (items.TryGetValue(parentId, out var parent) && parent.Deleted == null && parent.Folder != null && parent.Id != rootId)
            {
                changes.Add(parentId, item);
                continue;
            }

            roots.Add((item, await ResolveFolderPathAsync(parentId, map, folderPaths, cancellationToken).ConfigureAwait(false)));
        }

        Log.WriteVerboseMessage(LOGTAG, "DriveDeltaRead", "The delta of drive {0} reported {1} items, of which {2} are listed at the top level and {3} folders were moved", drive.Id, reported.Count, roots.Count, changes.MovedFolders.Count);
        return new IncrementalPlan(changes, roots, newToken);
    }

    /// <summary>
    /// Whether a folder or one of its ancestors was moved, in which case everything below
    /// it is listed by the walk of the moved folder
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <param name="map">The folder map</param>
    /// <param name="movedFolders">The ids of the moved folders</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns><c>true</c> if the folder is at or below a moved folder</returns>
    private async Task<bool> ChainCrossesMovedFolderAsync(string folderId, DriveFolderMap map, HashSet<string> movedFolders, CancellationToken cancellationToken)
    {
        if (movedFolders.Count == 0)
            return false;

        var current = folderId;
        for (var depth = 0; depth < 1024 && current != map.RootId; depth++)
        {
            if (movedFolders.Contains(current))
                return true;

            current = await GetParentAsync(current, map, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Resolves the entry path of a folder from the chain of its ancestors
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <param name="map">The folder map</param>
    /// <param name="cache">The paths resolved so far</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The folder path, ending with the directory separator</returns>
    private async Task<string> ResolveFolderPathAsync(string folderId, DriveFolderMap map, Dictionary<string, string> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(folderId, out var cached))
            return cached;

        // Walk up to the first known path, then build the paths back down
        var chain = new List<string>();
        var current = folderId;
        while (!cache.TryGetValue(current, out cached))
        {
            if (chain.Count > 1024 || chain.Contains(current))
                throw new UserInformationException($"The folder map of drive {drive.Id} has a cycle at folder {current}", "DriveFolderMapCycle");

            chain.Add(current);
            current = await GetParentAsync(current, map, cancellationToken).ConfigureAwait(false);
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            cached = Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(cached, chain[i]));
            cache[chain[i]] = cached;
        }

        return cached;
    }

    /// <summary>
    /// Gets the parent of a folder from the map, asking Graph for a folder the map does not
    /// know, which happens when the folder was not walked in a previous backup
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <param name="map">The folder map</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The parent id</returns>
    private async Task<string> GetParentAsync(string folderId, DriveFolderMap map, CancellationToken cancellationToken)
    {
        if (map.TryGetParent(folderId, out var parentId))
            return parentId;

        Log.WriteVerboseMessage(LOGTAG, "DriveFolderMapMiss", "Folder {0} of drive {1} is not in the stored folder map, looking it up", folderId, drive.Id);
        var folder = await provider.OneDriveApi.GetDriveItemAsync(drive.Id, folderId, cancellationToken).ConfigureAwait(false);
        parentId = folder.ParentReference?.Id
            ?? throw new UserInformationException($"Folder {folderId} of drive {drive.Id} has no parent", "DriveFolderWithoutParent");

        map.SetParent(folderId, parentId);
        return parentId;
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", drive.Id },
                { "o365:Type", SourceItemType.Drive.ToString() },
                { "o365:Name", drive.Name ?? "" },
                { "o365:Description", drive.Description ?? "" },
                { "o365:WebUrl", drive.WebUrl ?? "" },
                { "o365:CreatedBy", JsonSerializer.Serialize(drive.CreatedBy) ?? "" },
                { "o365:LastModifiedBy", JsonSerializer.Serialize(drive.LastModifiedBy) ?? "" },
                { "o365:DriveType", drive.DriveType ?? "" },
                { "o365:Owner", JsonSerializer.Serialize(drive.Owner) ?? "" },
                { "o365:Quota", JsonSerializer.Serialize(drive.Quota) ?? "" },
                { "o365:SharePointIds", JsonSerializer.Serialize(drive.SharePointIds) ?? "" },
                { "o365:System", JsonSerializer.Serialize(drive.System) ?? "" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
