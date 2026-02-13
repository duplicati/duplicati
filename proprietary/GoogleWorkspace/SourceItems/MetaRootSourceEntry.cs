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
            var service = provider.ApiHelper.GetDirectoryService();
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
            var service = provider.ApiHelper.GetDirectoryService();
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
            yield return new SharedDrivesSourceEntry(provider, this.Path, null);
        }
        else if (type == SourceItemType.MetaRootSites)
        {
            yield return new SitesSourceEntry(provider, this.Path);
        }
        else if (type == SourceItemType.MetaRootOrganizationalUnits)
        {
            // OrganizationalUnits is a single file containing all OUs, so we check license once
            if (!provider.LicenseApprovedForEntry(Path, GoogleRootType.OrganizationalUnits, "organizational_units"))
                yield break;

            yield return new OrganizationalUnitsSourceEntry(provider, this.Path);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "gsuite:v", "1" },
                { "gsuite:Type", type.ToString() },
                { "gsuite:Name", System.IO.Path.GetFileName(Path) },
                { "gsuite:Id", System.IO.Path.GetFileName(Path) }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
