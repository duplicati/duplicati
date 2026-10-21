// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

/// <summary>
/// The changes a drive delta walk reported, arranged as a tree so the entries can be
/// listed below their changed parents. Items whose parent is unchanged are listed at
/// their resolved path; the unchanged parent is carried forward by the backup.
/// </summary>
internal sealed class DriveChangeSet
{
    private static readonly string LOGTAG = Log.LogTagFromType<DriveChangeSet>();

    /// <summary>
    /// The folder map, updated with the changes
    /// </summary>
    public DriveFolderMap Map { get; }

    /// <summary>
    /// The ids of the folders that were moved to another parent. The delta reports only the
    /// folder, so its contents are listed again with the children requests at the new path.
    /// </summary>
    public HashSet<string> MovedFolders { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The changed items by the id of their parent
    /// </summary>
    private readonly Dictionary<string, List<GraphDriveItem>> _childrenByParent = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a change set over a folder map
    /// </summary>
    /// <param name="map">The folder map</param>
    public DriveChangeSet(DriveFolderMap map)
    {
        Map = map;
    }

    /// <summary>
    /// Records a changed item below its parent
    /// </summary>
    /// <param name="parentId">The parent id</param>
    /// <param name="item">The item</param>
    public void Add(string parentId, GraphDriveItem item)
    {
        if (!_childrenByParent.TryGetValue(parentId, out var list))
            _childrenByParent[parentId] = list = [];
        list.Add(item);
    }

    /// <summary>
    /// Gets the changed items below a folder
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <returns>The changed items</returns>
    public IReadOnlyList<GraphDriveItem> GetChildren(string folderId)
        => _childrenByParent.TryGetValue(folderId, out var list) ? list : [];

    /// <summary>
    /// Creates the entry for a changed item. A moved folder is listed in full at its new
    /// path; any other folder lists only its changed children.
    /// </summary>
    /// <param name="provider">The provider</param>
    /// <param name="drive">The drive</param>
    /// <param name="item">The item</param>
    /// <param name="parentPath">The path of the item's parent</param>
    /// <returns>The entry, or <c>null</c> if the item is of a kind that is not backed up</returns>
    public ISourceProviderEntry? CreateEntry(SourceProvider provider, GraphDrive drive, GraphDriveItem item, string parentPath)
    {
        if (item.Folder != null)
            return MovedFolders.Contains(item.Id)
                ? new DriveFolderSourceEntry(provider, parentPath, drive, item, Map)
                : new DriveFolderSourceEntry(provider, parentPath, drive, item, this);

        if (item.File != null)
            return new DriveFileSourceEntry(provider, parentPath, drive, item);

        Log.WriteWarningMessage(LOGTAG, "UnknownDriveItemType", null, $"Unknown drive item type: {item.Id}");
        return null;
    }
}
