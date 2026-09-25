// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Google.Apis.Drive.v3;
using Drive = Google.Apis.Drive.v3.Data.Drive;
using File = Google.Apis.Drive.v3.Data.File;
using Group = Google.Apis.Admin.Directory.directory_v1.Data.Group;
using User = Google.Apis.Admin.Directory.directory_v1.Data.User;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Paged listings of the top-level items of the tenant, shared between the enumeration of the
/// backup source and the item count reported to the user interface.
/// </summary>
public sealed partial class SourceProvider
{
    /// <summary>
    /// The largest page the Directory API allows for user listings.
    /// </summary>
    private const int DIRECTORY_USERS_PAGE_SIZE = 500;

    /// <summary>
    /// The largest page the Directory API allows for group listings.
    /// </summary>
    private const int DIRECTORY_GROUPS_PAGE_SIZE = 200;

    /// <summary>
    /// The largest page the Drive API allows for shared drive listings.
    /// </summary>
    private const int DRIVE_SHARED_DRIVES_PAGE_SIZE = 100;

    /// <summary>
    /// The largest page the Drive API allows for file listings.
    /// </summary>
    private const int DRIVE_FILES_PAGE_SIZE = 1000;

    /// <summary>
    /// Lists the users of the tenant, carrying only what is needed to identify each account and
    /// to decide whether it consumes a seat (see <see cref="UserCountsAsSeat"/>). The full
    /// profile is read separately when the user is backed up.
    /// </summary>
    /// <remarks>
    /// The seat decision is made from the listing alone, so no request is made per user and the
    /// cost is proportional to the size of the tenant divided by the page size. Users without an
    /// id or a primary email are skipped, as neither the seat counter nor the user-scoped APIs
    /// can address them.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous enumerable of the users.</returns>
    internal async IAsyncEnumerable<User> ListAllUsersAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = _apiHelper.GetDirectoryServiceForUsers();
        var request = service.Users.List();
        request.Customer = "my_customer";
        request.MaxResults = DIRECTORY_USERS_PAGE_SIZE;
        request.Fields = "nextPageToken,users(id,primaryEmail,suspended,archived)";

        string? nextPageToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.PageToken = nextPageToken;
            var users = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            foreach (var user in users?.UsersValue ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(user.Id) || string.IsNullOrEmpty(user.PrimaryEmail))
                    continue;

                yield return user;
            }

            nextPageToken = users?.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    /// <summary>
    /// Lists the groups of the tenant.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous enumerable of the groups.</returns>
    internal async IAsyncEnumerable<Group> ListAllGroupsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = _apiHelper.GetDirectoryServiceForGroups();
        var request = service.Groups.List();
        request.Customer = "my_customer";
        request.MaxResults = DIRECTORY_GROUPS_PAGE_SIZE;

        string? nextPageToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.PageToken = nextPageToken;
            var groups = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            foreach (var group in groups?.GroupsValue ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(group.Id))
                    continue;

                yield return group;
            }

            nextPageToken = groups?.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    /// <summary>
    /// Lists the shared drives visible to the given Drive service, which is either the
    /// tenant-wide service or one impersonating a specific user.
    /// </summary>
    /// <param name="driveService">The Drive service to list with.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous enumerable of the shared drives.</returns>
    internal static async IAsyncEnumerable<Drive> ListAllSharedDrivesAsync(DriveService driveService, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = driveService.Drives.List();
        request.PageSize = DRIVE_SHARED_DRIVES_PAGE_SIZE;

        string? nextPageToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.PageToken = nextPageToken;
            var drives = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            foreach (var drive in drives?.Drives ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(drive.Id))
                    continue;

                yield return drive;
            }

            nextPageToken = drives?.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    /// <summary>
    /// Lists the Google Sites of the tenant, which Drive stores as files of the site MIME type
    /// across all drives.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous enumerable of the site files.</returns>
    internal async IAsyncEnumerable<File> ListAllSitesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = _apiHelper.GetDriveService();
        var request = service.Files.List();
        request.Q = $"mimeType='{GoogleMimeTypes.Site}' and trashed=false";
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;
        // Request all drives so we do not get user drives only
        request.Corpora = "allDrives";
        request.PageSize = DRIVE_FILES_PAGE_SIZE;

        string? nextPageToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.PageToken = nextPageToken;
            var files = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            foreach (var file in files?.Files ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(file.Id))
                    continue;

                yield return file;
            }

            nextPageToken = files?.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }
}
