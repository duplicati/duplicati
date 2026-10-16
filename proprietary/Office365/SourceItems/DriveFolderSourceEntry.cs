// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

/// <summary>
/// A folder in a drive. It either lists its children with a request to Graph, recording
/// the folders it finds in the drive's folder map, or lists a prepared set of changed
/// children from a delta walk.
/// </summary>
internal class DriveFolderSourceEntry : MetaEntryBase
{
    private static readonly string LOGTAG = Log.LogTagFromType<DriveFolderSourceEntry>();

    private readonly SourceProvider provider;
    private readonly GraphDrive drive;
    private readonly GraphDriveItem item;
    private readonly DriveFolderMap? map;
    private readonly DriveChangeSet? changes;

    /// <summary>
    /// Creates a folder entry that lists its children with a request to Graph
    /// </summary>
    /// <param name="provider">The provider</param>
    /// <param name="path">The path of the parent</param>
    /// <param name="drive">The drive</param>
    /// <param name="item">The folder item</param>
    /// <param name="map">The folder map to record child folders in, or <c>null</c> if changes are not tracked</param>
    public DriveFolderSourceEntry(SourceProvider provider, string path, GraphDrive drive, GraphDriveItem item, DriveFolderMap? map)
        : base(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, item.Id)), item.CreatedDateTime.FromGraphDateTime(), item.LastModifiedDateTime.FromGraphDateTime())
    {
        this.provider = provider;
        this.drive = drive;
        this.item = item;
        this.map = map;
        this.changes = null;
    }

    /// <summary>
    /// Creates a folder entry that lists only the changed children a delta walk reported
    /// </summary>
    /// <param name="provider">The provider</param>
    /// <param name="path">The path of the parent</param>
    /// <param name="drive">The drive</param>
    /// <param name="item">The folder item</param>
    /// <param name="changes">The changes reported by the delta walk</param>
    public DriveFolderSourceEntry(SourceProvider provider, string path, GraphDrive drive, GraphDriveItem item, DriveChangeSet changes)
        : base(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, item.Id)), item.CreatedDateTime.FromGraphDateTime(), item.LastModifiedDateTime.FromGraphDateTime())
    {
        this.provider = provider;
        this.drive = drive;
        this.item = item;
        this.map = changes.Map;
        this.changes = changes;
    }

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (changes != null)
        {
            foreach (var child in changes.GetChildren(item.Id))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                var entry = changes.CreateEntry(provider, drive, child, this.Path);
                if (entry != null)
                    yield return entry;
            }

            yield break;
        }

        // The backup skips a folder it cannot list and continues, so the failure is reported
        // to the change tracker: the drive's token must not claim the folder was walked
        var stateKey = provider.DriveDeltaEnabled ? ChangeTrackingKeys.Drive(drive.Id) : null;

        // The folder is listed in full, so below an incrementally walked drive it is the one
        // place where an empty listing means an empty folder
        if (stateKey != null && provider.ChangeTracker.IsActive)
            provider.ChangeTracker.MarkExaminedSubtree(this.Path);
        var children = provider.ListWithFailureTrackingAsync(stateKey, provider.OneDriveApi.ListDriveFolderChildrenAsync(drive.Id, item.Id, cancellationToken), cancellationToken);
        await foreach (var child in children.ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (child.Deleted != null)
                continue;
            if (child.Folder != null)
            {
                map?.SetParent(child.Id, item.Id);
                yield return new DriveFolderSourceEntry(provider, this.Path, drive, child, map);
            }
            else if (child.File != null)
                yield return new DriveFileSourceEntry(provider, this.Path, drive, child);
            else
                Log.WriteWarningMessage(LOGTAG, "UnknownDriveItemType", null, $"Unknown drive item type: {child.Id}");
        }
    }

    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", item.Id },
                { "o365:Type", SourceItemType.DriveFolder.ToString() },
                { "o365:Name", item.Name ?? "" },
                { "o365:FolderChildCount", item.Folder?.ChildCount.ToString() ?? "" },
                { "o365:ETag", item.ETag ?? "" },
                { "o365:CTag", item.CTag ?? "" },
                { "o365:ParentReference", JsonSerializer.Serialize(item.ParentReference) ?? "" },
                { "o365:FileSystemInfo", JsonSerializer.Serialize(item.FileSystemInfo) ?? "" },
                { "o365:DownloadUrl", item.DownloadUrl ?? "" }
            };

        // Only folders carrying the "shared" facet have sharing links or permissions of their
        // own; everything else inherits from its parent, so asking Graph would cost a request
        // per folder and return nothing worth storing.
        if (item.Shared.HasValue)
        {
            try
            {
                var permissions = new List<GraphPermission>();
                await foreach (var perm in provider.OneDriveApi.GetDriveItemPermissionsAsync(drive.Id, item.Id, cancellationToken))
                {
                    permissions.Add(perm);
                }

                if (permissions.Count > 0)
                {
                    metadata["o365:Permissions"] = JsonSerializer.Serialize(permissions);
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "PermissionReadError", ex, $"Failed to read permissions for folder {item.Id}");
            }
        }

        return metadata
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public override Task<bool> FileExists(string filename, CancellationToken cancellationToken)
    {
        // TODO: Implement this
        return Task.FromResult(false);
    }
}
