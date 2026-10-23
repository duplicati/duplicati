// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.ChangeTracking;
using Google.Apis.Drive.v3;
using File = Google.Apis.Drive.v3.Data.File;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// The changes a drive's changes feed reported, arranged as a tree so the entries can be
/// listed below their changed parents. Items whose parent is unchanged are listed at
/// their resolved path; the unchanged parent is carried forward by the backup.
/// </summary>
internal sealed class DriveChangeSet
{
    /// <summary>
    /// The folder map, updated with the changes
    /// </summary>
    public FolderMap Map { get; }

    /// <summary>
    /// The ids of the folders that were moved to another parent, or that the map did not know.
    /// The feed reports only the folder, so its contents are listed again with the children
    /// requests at the new path.
    /// </summary>
    public HashSet<string> MovedFolders { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The changed items by the id of their parent
    /// </summary>
    private readonly Dictionary<string, List<File>> _childrenByParent = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a change set over a folder map
    /// </summary>
    /// <param name="map">The folder map</param>
    public DriveChangeSet(FolderMap map)
    {
        Map = map;
    }

    /// <summary>
    /// Records a changed item below its parent
    /// </summary>
    /// <param name="parentId">The parent id</param>
    /// <param name="file">The item</param>
    public void Add(string parentId, File file)
    {
        if (!_childrenByParent.TryGetValue(parentId, out var list))
            _childrenByParent[parentId] = list = [];
        list.Add(file);
    }

    /// <summary>
    /// Gets the changed items below a folder
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <returns>The changed items</returns>
    public IReadOnlyList<File> GetChildren(string folderId)
        => _childrenByParent.TryGetValue(folderId, out var list) ? list : [];

    /// <summary>
    /// Creates the entry for a changed item. A moved folder is listed in full at its new
    /// path; any other folder lists only its changed children.
    /// </summary>
    /// <param name="provider">The provider</param>
    /// <param name="stateKey">The change tracking key of the drive</param>
    /// <param name="userId">The user the drive is listed through, or <c>null</c> at the tenant level</param>
    /// <param name="userIsInactive">Whether the user is suspended or archived</param>
    /// <param name="driveService">The Drive service</param>
    /// <param name="file">The item</param>
    /// <param name="parentPath">The path of the item's parent</param>
    /// <returns>The entry</returns>
    public ISourceProviderEntry CreateEntry(SourceProvider provider, string stateKey, string? userId, bool userIsInactive, DriveService driveService, File file, string parentPath)
    {
        if (GoogleMimeTypes.IsFolder(file.MimeType))
            return MovedFolders.Contains(file.Id)
                ? new DriveFolderSourceEntry(provider, stateKey, parentPath, userId, userIsInactive, file.Name, file.Id, driveService, Map)
                : new DriveFolderSourceEntry(provider, stateKey, parentPath, userId, userIsInactive, file.Name, file.Id, driveService, this);

        return new DriveFileSourceEntry(provider, stateKey, parentPath, file, userIsInactive, driveService);
    }
}
