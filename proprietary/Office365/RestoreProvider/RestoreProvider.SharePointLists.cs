// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

public partial class RestoreProvider
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _restoredSharePointListMap = new();

    private async Task RestoreSharePointLists(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var lists = GetMetadataByType(SourceItemType.SharePointList);
        if (lists.Count == 0)
            return;

        string? targetSiteId = null;
        if (RestoreTarget.Type == SourceItemType.Site)
        {
            targetSiteId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetSiteId))
        {
            // Try to infer from path if possible, or log error
            // For now, we assume we are restoring to a site
            return;
        }

        foreach (var list in lists)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = list.Key;
                var metadata = list.Value;
                var displayName = metadata.GetValueOrDefault("o365:DisplayName") ?? "Restored List";
                var listInfoJson = metadata.GetValueOrDefault("o365:ListInfo");
                GraphListInfo? listInfo = null;
                if (!string.IsNullOrEmpty(listInfoJson))
                {
                    listInfo = JsonSerializer.Deserialize<GraphListInfo>(listInfoJson);
                }

                _metadata.TryRemove(originalPath, out _);

                // Check if list exists
                var existingList = await SourceProvider.SharePointListApi.GetListAsync(targetSiteId, displayName, cancel);
                if (existingList != null)
                {
                    _restoredSharePointListMap[originalPath] = existingList.Id;
                }
                else
                {
                    var newList = await SourceProvider.SharePointListApi.CreateListAsync(targetSiteId, displayName, listInfo, cancel);
                    _restoredSharePointListMap[originalPath] = newList.Id;
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreSharePointListFailed", ex, $"Failed to restore list {list.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreSharePointListItems(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var items = GetMetadataByType(SourceItemType.SharePointListItem);
        if (items.Count == 0)
            return;

        string? targetSiteId = null;
        if (RestoreTarget.Type == SourceItemType.Site)
        {
            targetSiteId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetSiteId))
            return;

        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = item.Key;

                // Determine List ID
                // Path: .../ListId/ItemId.json
                var parent = Path.GetDirectoryName(originalPath);
                if (parent == null) continue;

                string? listId = null;
                if (_restoredSharePointListMap.TryGetValue(parent, out var mappedListId))
                {
                    listId = mappedListId;
                }
                else
                {
                    // Maybe we are restoring to the same list ID if we didn't restore the list itself (e.g. partial restore)
                    // But we don't know the target list ID unless we mapped it.
                    // If we didn't restore the list, we might assume the parent folder name is the list ID?
                    // But the parent folder name is the source list ID.
                    // If we are restoring to the same site, maybe the list exists with the same ID? Unlikely.
                    // If we are restoring to a different site, we definitely need the map.
                    // If the list was not in the backup (e.g. only items selected), we can't restore items easily without knowing where to put them.
                    // We'll skip if we can't find the list.
                    Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListItemMissingList", null, $"Missing target list for item {originalPath}, skipping.");
                    continue;
                }

                var contentPath = SystemIO.IO_OS.PathCombine(originalPath, "content.json");
                var contentEntry = _temporaryFiles.GetValueOrDefault(contentPath);
                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListItemMissingContent", null, $"Missing content for item {originalPath}, skipping.");
                    continue;
                }

                GraphListItem? itemData = null;
                using (var stream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    itemData = await JsonSerializer.DeserializeAsync<GraphListItem>(stream, cancellationToken: cancel);
                }

                if (itemData?.Fields != null)
                {
                    await SourceProvider.SharePointListApi.CreateListItemAsync(targetSiteId, listId, itemData.Fields.Value, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreSharePointListItemFailed", ex, $"Failed to restore list item {item.Key}: {ex.Message}");
            }
        }
    }
}
