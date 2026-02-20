// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class MetaRootSourceEntry(SourceProvider provider, string parentPath, string name, SourceItemType type)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, name)), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            yield break;

        if (type == SourceItemType.MetaRootUsers)
        {
            var service = provider.ApiHelper.GetDirectoryServiceForUsers();
            var request = service.Users.List();
            request.Customer = "my_customer";

            string? nextPageToken = null;

            do
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                request.PageToken = nextPageToken;
                var users = await request.ExecuteAsync(cancellationToken);

                if (users != null && users.UsersValue != null)
                {
                    foreach (var user in users.UsersValue)
                    {
                        if (cancellationToken.IsCancellationRequested) yield break;

                        if (!provider.LicenseApprovedForEntry(Path, GoogleRootType.Users, user.Id))
                            yield break;

                        yield return new UserSourceEntry(provider, this.Path, user.PrimaryEmail);
                    }
                }

                nextPageToken = users?.NextPageToken;
            } while (!string.IsNullOrEmpty(nextPageToken));
        }
        else if (type == SourceItemType.MetaRootGroups)
        {
            var service = provider.ApiHelper.GetDirectoryServiceForGroups();
            var request = service.Groups.List();
            request.Customer = "my_customer";

            string? nextPageToken = null;
            do
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                request.PageToken = nextPageToken;
                var groups = await request.ExecuteAsync(cancellationToken);

                if (groups.GroupsValue != null)
                {
                    foreach (var group in groups.GroupsValue)
                    {
                        if (cancellationToken.IsCancellationRequested) yield break;

                        if (!provider.LicenseApprovedForEntry(Path, GoogleRootType.Groups, group.Id))
                            yield break;

                        yield return new GroupSourceEntry(provider, this.Path, group);
                    }
                }
                nextPageToken = groups.NextPageToken;
            } while (!string.IsNullOrEmpty(nextPageToken));
        }
        else if (type == SourceItemType.MetaRootSharedDrives)
        {
            var driveService = provider.ApiHelper.GetDriveService();
            await foreach (var n in SharedDrivesSourceEntry.EnumerateSharedDrives(provider, this.Path, null, driveService, cancellationToken))
                yield return n;
        }
        else if (type == SourceItemType.MetaRootSites)
        {
            var service = provider.ApiHelper.GetDriveService();
            var request = service.Files.List();
            request.Q = $"mimeType='{GoogleMimeTypes.Site}' and trashed=false";
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;
            // Request all drives so we do not get user drives only
            request.Corpora = "allDrives";

            string? nextPageToken = null;
            do
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                request.PageToken = nextPageToken;

                Google.Apis.Drive.v3.Data.FileList? files = null;
                try
                {
                    files = await request.ExecuteAsync(cancellationToken);
                }
                catch
                {
                    // Fallback or ignore
                    yield break;
                }

                if (files != null && files.Files != null)
                {
                    foreach (var file in files.Files)
                    {
                        if (cancellationToken.IsCancellationRequested) yield break;

                        if (!provider.LicenseApprovedForEntry(Path, GoogleRootType.Sites, file.Id))
                            yield break;

                        yield return new SiteSourceEntry(this.Path, file);
                    }
                }
                nextPageToken = files?.NextPageToken;
            } while (!string.IsNullOrEmpty(nextPageToken));
        }
        else if (type == SourceItemType.MetaRootOrganizationalUnits)
        {
            yield return new OrganizationalUnitsSourceEntry(provider, this.Path);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "gsuite:v", "1" },
                { "gsuite:Type", type.ToString() },
                { "gsuite:Name", name },
                { "gsuite:Id", type.ToString() }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
