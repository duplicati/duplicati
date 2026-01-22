// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

public partial class RestoreProvider
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _restoredSharePointListMap = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _restoredSharePointFolderMap = new();
    private readonly Dictionary<string, HashSet<string>> _sharePointListExistingItems = new();

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

        // Sort items by path to ensure folders are created before their children
        var sortedItems = items.OrderBy(x => x.Key).ToList();

        foreach (var item in sortedItems)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = item.Key;

                // Determine List ID by walking up the path
                string? listId = null;
                string? listPath = null;
                var currentPath = originalPath;

                while (true)
                {
                    var parentDir = Util.AppendDirSeparator(Path.GetDirectoryName(currentPath.TrimEnd(Path.DirectorySeparatorChar)));
                    if (string.IsNullOrEmpty(parentDir)) break;

                    if (_restoredSharePointListMap.TryGetValue(parentDir, out var mappedListId))
                    {
                        listId = mappedListId;
                        listPath = parentDir;
                        break;
                    }
                    currentPath = parentDir;
                }

                if (listId == null || listPath == null)
                {
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
                    itemData = await JsonSerializer.DeserializeAsync<GraphListItem>(stream, cancellationToken: cancel);

                if (itemData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListItemDeserializationFailed", null, $"Failed to deserialize item {originalPath}, skipping.");
                    continue;
                }

                var isDocumentLibrary = await SourceProvider.SharePointListApi.IsDocumentLibraryAsync(targetSiteId, listId, cancel);
                if (isDocumentLibrary)
                {
                    var driveId = await SourceProvider.SharePointListApi.GetDriveIdForListAsync(targetSiteId, listId, cancel);
                    if (driveId == null)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListItemMissingDrive", null, $"Missing drive for document library list {listId}, skipping item {originalPath}.");
                        continue;
                    }

                    // Document library item - restore as DriveItem
                    string? fileName = null;
                    if (itemData.Fields != null && itemData.Fields.Value.TryGetProperty("FileLeafRef", out var nameProp))
                    {
                        fileName = nameProp.GetString();
                    }

                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListItemMissingName", null, $"Missing FileLeafRef for item {originalPath}, skipping.");
                        continue;
                    }

                    bool isFolder = itemData.ContentType?.Name == "Folder" || itemData.ContentType?.Name == "Document Set";
                    if (itemData.Fields != null && itemData.Fields.Value.TryGetProperty("FSObjType", out var fsObjTypeProp))
                    {
                        if (fsObjTypeProp.GetString() == "1") isFolder = true;
                    }

                    // Determine parent ID
                    string parentId;
                    var itemParentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));

                    try
                    {
                        parentId = await EnsureParentFolderAsync(driveId, listPath, itemParentPath, cancel);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListItemMissingParent", ex, $"Failed to ensure parent folder for item {originalPath}, skipping.");
                        continue;
                    }

                    if (!_ignoreExisting)
                    {
                        var existingItem = await DriveApi.GetDriveItemAsync(driveId, parentId, fileName, cancel);
                        if (existingItem != null)
                        {
                            Log.WriteInformationMessage(LOGTAG, "RestoreSharePointListItemSkipped", $"Skipping restore of item {originalPath} because it already exists.");

                            if (isFolder)
                            {
                                _restoredSharePointFolderMap[originalPath] = existingItem.Id;
                            }

                            _metadata.TryRemove(originalPath, out _);
                            _temporaryFiles.TryRemove(originalPath, out var contentFileSkipped);
                            contentFileSkipped?.Dispose();
                            continue;
                        }
                    }

                    if (isFolder)
                    {
                        var newFolder = await DriveApi.CreateDriveFolderAsync(driveId, parentId, fileName, cancel);
                        _restoredSharePointFolderMap[originalPath] = newFolder.Id;
                    }
                    else
                    {
                        var dataPath = SystemIO.IO_OS.PathCombine(originalPath, "content.data");
                        var dataEntry = _temporaryFiles.GetValueOrDefault(dataPath);

                        if (dataEntry != null)
                        {
                            using (var contentStream = SystemIO.IO_OS.FileOpenRead(dataEntry))
                            {
                                await DriveApi.RestoreDriveItemToFolderAsync(driveId, parentId, fileName, contentStream, null, cancel);
                            }
                        }
                        else
                        {
                            Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListItemMissingFileContent", null, $"Missing content.data for item {originalPath}, skipping file restore.");
                        }
                    }

                    continue;
                }

                if (itemData?.Fields == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListItemMissingFields", null, $"Missing fields for item {originalPath}, skipping.");
                    continue;
                }

                if (!_ignoreExisting)
                {
                    string? title = null;
                    if (itemData.Fields.Value.ValueKind == JsonValueKind.Object &&
                        itemData.Fields.Value.TryGetProperty("Title", out var titleProp))
                    {
                        title = titleProp.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        if (!_sharePointListExistingItems.TryGetValue(listId, out var existingItems))
                        {
                            existingItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            try
                            {
                                await foreach (var existingItem in SourceProvider.SharePointListApi.ListListItemsAsync(targetSiteId, listId, cancel))
                                {
                                    if (existingItem.Fields.HasValue &&
                                        existingItem.Fields.Value.ValueKind == JsonValueKind.Object &&
                                        existingItem.Fields.Value.TryGetProperty("Title", out var existingTitleProp))
                                    {
                                        var existingTitle = existingTitleProp.GetString();
                                        if (!string.IsNullOrWhiteSpace(existingTitle))
                                        {
                                            existingItems.Add(existingTitle);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListFetchFailed", ex, $"Failed to fetch existing items for list {listId}: {ex.Message}");
                            }
                            _sharePointListExistingItems[listId] = existingItems;
                        }

                        if (existingItems.Contains(title))
                        {
                            Log.WriteInformationMessage(LOGTAG, "RestoreSharePointListItemSkipped", $"Skipping restore of item {originalPath} because it already exists.");
                            _metadata.TryRemove(originalPath, out _);
                            _temporaryFiles.TryRemove(originalPath, out var contentFileSkipped);
                            contentFileSkipped?.Dispose();
                            continue;
                        }
                    }

                    var res = await SourceProvider.SharePointListApi.CreateListItemAsync(targetSiteId, listId, itemData.Fields.Value, cancel);
                    if (res == null)
                        Log.WriteWarningMessage(LOGTAG, "RestoreSharePointListItemFailed", null, $"Failed to create list item for {originalPath} (no restorable fields), skipping.");
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

    private async Task<string> EnsureParentFolderAsync(string driveId, string listPath, string currentPath, CancellationToken cancel)
    {
        if (currentPath.Equals(listPath, StringComparison.OrdinalIgnoreCase))
            return "root";

        if (_restoredSharePointFolderMap.TryGetValue(currentPath, out var cachedId))
            return cachedId;

        var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(currentPath.TrimEnd(Path.DirectorySeparatorChar)));

        // Safety check to prevent infinite recursion if we go above listPath
        if (string.IsNullOrEmpty(parentPath) || currentPath.Length <= listPath.Length)
            throw new InvalidOperationException($"Path {currentPath} cannot be resolved relative to {listPath}");

        var parentId = await EnsureParentFolderAsync(driveId, listPath, parentPath, cancel);

        var folderName = Path.GetFileName(currentPath.TrimEnd(Path.DirectorySeparatorChar));

        var existing = await DriveApi.GetDriveItemAsync(driveId, parentId, folderName, cancel);
        if (existing != null)
        {
            _restoredSharePointFolderMap[currentPath] = existing.Id;
            return existing.Id;
        }

        var newFolder = await DriveApi.CreateDriveFolderAsync(driveId, parentId, folderName, cancel);
        _restoredSharePointFolderMap[currentPath] = newFolder.Id;
        return newFolder.Id;
    }
}
