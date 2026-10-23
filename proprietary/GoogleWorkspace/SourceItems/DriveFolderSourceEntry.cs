// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.ChangeTracking;
using Google.Apis.Drive.v3;
using System.Runtime.CompilerServices;
using File = Google.Apis.Drive.v3.Data.File;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// A folder in a drive. It either lists its children with a request to Drive, recording
/// the folders it finds in the drive's folder map, or lists a prepared set of changed
/// children from the drive's changes feed.
/// </summary>
internal class DriveFolderSourceEntry : MetaEntryBase
{
    /// <summary>
    /// The fields requested for every listed file
    /// </summary>
    internal const string FILE_FIELDS = "id, name, mimeType, createdTime, modifiedTime, size, description, parents, trashed, driveId";

    private readonly SourceProvider provider;
    private readonly string? stateKey;
    private readonly string? userId;
    private readonly bool userIsInactive;
    private readonly string name;
    private readonly string folderId;
    private readonly DriveService driveService;
    private readonly FolderMap? map;
    private readonly DriveChangeSet? changes;

    /// <summary>
    /// Creates a folder entry that lists its children with a request to Drive
    /// </summary>
    /// <param name="provider">The provider</param>
    /// <param name="stateKey">The change tracking key of the drive, or <c>null</c> if changes are not tracked</param>
    /// <param name="parentPath">The path of the parent</param>
    /// <param name="userId">The user the drive is listed through, or <c>null</c> at the tenant level</param>
    /// <param name="userIsInactive">Whether the user is suspended or archived</param>
    /// <param name="name">The folder name</param>
    /// <param name="folderId">The folder id</param>
    /// <param name="driveService">The Drive service</param>
    /// <param name="map">The folder map to record child folders in, or <c>null</c> if changes are not tracked</param>
    public DriveFolderSourceEntry(SourceProvider provider, string? stateKey, string parentPath, string? userId, bool userIsInactive, string name, string folderId, DriveService driveService, FolderMap? map)
        : this(provider, stateKey, parentPath, userId, userIsInactive, name, folderId, driveService, map, null)
    {
    }

    /// <summary>
    /// Creates a folder entry that lists only the changed children the changes feed reported
    /// </summary>
    /// <param name="provider">The provider</param>
    /// <param name="stateKey">The change tracking key of the drive</param>
    /// <param name="parentPath">The path of the parent</param>
    /// <param name="userId">The user the drive is listed through, or <c>null</c> at the tenant level</param>
    /// <param name="userIsInactive">Whether the user is suspended or archived</param>
    /// <param name="name">The folder name</param>
    /// <param name="folderId">The folder id</param>
    /// <param name="driveService">The Drive service</param>
    /// <param name="changes">The changes reported by the feed</param>
    public DriveFolderSourceEntry(SourceProvider provider, string stateKey, string parentPath, string? userId, bool userIsInactive, string name, string folderId, DriveService driveService, DriveChangeSet changes)
        : this(provider, stateKey, parentPath, userId, userIsInactive, name, folderId, driveService, changes.Map, changes)
    {
    }

    private DriveFolderSourceEntry(SourceProvider provider, string? stateKey, string parentPath, string? userId, bool userIsInactive, string name, string folderId, DriveService driveService, FolderMap? map, DriveChangeSet? changes)
        : base(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, folderId)), null, null)
    {
        this.provider = provider;
        this.stateKey = stateKey;
        this.userId = userId;
        this.userIsInactive = userIsInactive;
        this.name = name;
        this.folderId = folderId;
        this.driveService = driveService;
        this.map = map;
        this.changes = changes;
    }

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (changes != null && stateKey != null)
        {
            foreach (var child in changes.GetChildren(folderId))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return changes.CreateEntry(provider, stateKey, userId, userIsInactive, driveService, child, this.Path);
            }

            yield break;
        }

        // The folder is listed in full, so below an incrementally walked drive it is the one
        // place where an empty listing means an empty folder, and the paths below it must
        // not be carried forward. Anywhere else an unmarked path already counts as examined,
        // so the mark is only recorded where it changes an answer
        var tracker = provider.ChangeTracker;
        if (stateKey != null && tracker.IsActive && tracker.IsIncrementallyEnumeratedFolder(this.Path))
            tracker.MarkExaminedSubtree(this.Path);

        // The backup skips a folder it cannot list and continues, so the failure is reported
        // to the change tracker: the drive's token must not claim the folder was walked
        await foreach (var file in tracker.ListWithFailureTrackingAsync(stateKey, ListChildrenAsync(driveService, folderId, cancellationToken), cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            if (GoogleMimeTypes.IsFolder(file.MimeType))
            {
                map?.SetParent(file.Id, folderId);
                yield return new DriveFolderSourceEntry(provider, stateKey, this.Path, userId, userIsInactive, file.Name, file.Id, driveService, map);
            }
            else
            {
                yield return new DriveFileSourceEntry(provider, stateKey, this.Path, file, userIsInactive, driveService);
            }
        }
    }

    /// <summary>
    /// Lists the children of a folder that are not in the trash
    /// </summary>
    /// <param name="driveService">The Drive service</param>
    /// <param name="folderId">The folder id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The children</returns>
    internal static async IAsyncEnumerable<File> ListChildrenAsync(DriveService driveService, string folderId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = driveService.Files.List();
        request.Q = $"'{folderId}' in parents and trashed = false";
        request.Fields = $"nextPageToken, files({FILE_FIELDS})";
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var files = await request.ExecuteAsync(cancellationToken);

            foreach (var file in files.Files ?? [])
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                if (string.IsNullOrEmpty(file.Id))
                    continue;

                yield return file;
            }

            nextPageToken = files.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
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
