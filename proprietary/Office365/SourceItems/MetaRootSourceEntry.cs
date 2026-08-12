// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

[Flags]
internal enum Office365MetaType
{
    // Tenant directory / discovery roots
    Users = 1,
    Groups = 2,
    Sites = 4,

    // // Compliance/audit
    // AuditLogBlobs        // Management Activity API content items
}

internal class MetaRootSourceEntry(SourceProvider provider, string mountPoint, Office365MetaType type)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(mountPoint, type.ToString().ToLower())), null, null)
{
    private static readonly string LOGTAG = Log.LogTagFromType<MetaRootSourceEntry>();

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Type", type switch
                {
                    Office365MetaType.Users => SourceItemType.MetaRootUsers.ToString(),
                    Office365MetaType.Groups => SourceItemType.MetaRootGroups.ToString(),
                    Office365MetaType.Sites => SourceItemType.MetaRootSites.ToString(),
                    _ => null
                } },
                { "o365:Name", type switch
                {
                    Office365MetaType.Users => "Users",
                    Office365MetaType.Groups => "Groups",
                    Office365MetaType.Sites => "Sites",
                    _ => null
                } },
                { "o365:MetaType", type.ToString() },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        switch (type)
        {
            case Office365MetaType.Users:
                // Microsoft Graph paging over users is not guaranteed to be stable, so the
                // same user can be returned on more than one page. De-duplicate by user id
                // to avoid emitting duplicate paths for the same user.
                var seenUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                await foreach (var user in provider.RootApi.ListAllUsersAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    if (string.IsNullOrEmpty(user.Id) || !seenUserIds.Add(user.Id))
                        continue;

                    // Skip users whose classification is excluded by the include filter. When
                    // every classification is included, which is the default, this does not
                    // classify the user and so makes no Graph call.
                    if (!await provider.IsUserClassificationIncludedAsync(user, cancellationToken).ConfigureAwait(false))
                        continue;

                    // Shared mailboxes without additional storage do not consume a seat, but
                    // determining that costs a Graph call per user and the gate below only
                    // needs the answer once the seat limit has been reached. Until then, assume
                    // the user consumes a seat: the gate approves either way, and nothing is
                    // counted here because this check does not increment.
                    var countsAsSeat = !provider.SeatLimitReached(type)
                        || await provider.UserCountsAsSeatAsync(user, cancellationToken).ConfigureAwait(false);

                    if (provider.LicenseApprovedForEntry(Path, type, user.Id, false, countsAsSeat))
                        yield return new UserSourceEntry(provider, Path, user);
                }
                break;
            case Office365MetaType.Groups:
                // Microsoft Graph paging over groups is not guaranteed to be stable, so the
                // same group can be returned on more than one page. De-duplicate by group id
                // to avoid emitting duplicate paths for the same group.
                var seenGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                await foreach (var group in provider.RootApi.ListAllGroupsAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    if (string.IsNullOrEmpty(group.Id) || !seenGroupIds.Add(group.Id))
                        continue;

                    // Skip groups whose classification is excluded by the include filter.
                    if (!provider.IsGroupClassificationIncluded(group))
                        continue;

                    // Security groups and distribution lists do not consume a seat;
                    // only Microsoft 365 (Unified) groups count.
                    var groupCountsAsSeat = SourceProvider.GroupCountsAsSeat(group);

                    if (provider.LicenseApprovedForEntry(Path, type, group.Id, false, groupCountsAsSeat))
                        yield return new GroupSourceEntry(provider, this.Path, group);
                }
                break;
            case Office365MetaType.Sites:
                // Microsoft Graph paging over sites is not guaranteed to be stable, so the
                // same site can be returned on more than one page. De-duplicate by site id
                // to avoid emitting duplicate paths for the same site.
                var seenSiteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                await foreach (var site in provider.RootApi.ListAllSitesAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    if (string.IsNullOrEmpty(site.Id) || !seenSiteIds.Add(site.Id))
                        continue;

                    // Skip sites whose classification is excluded by the include filter. When
                    // every classification is included, which is the default, this does not
                    // classify the site and so makes no Graph call.
                    if (!await provider.IsSiteClassificationIncludedAsync(site, cancellationToken).ConfigureAwait(false))
                        continue;

                    // Personal sites of disabled users do not consume a seat, but determining
                    // that requires the disabled-user lookup and the gate below only needs the
                    // answer once the seat limit has been reached. Until then, assume the site
                    // consumes a seat: the gate approves either way, and nothing is counted
                    // here because this check does not increment.
                    var siteCountsAsSeat = !provider.SeatLimitReached(type)
                        || await provider.SiteCountsAsSeatAsync(site, cancellationToken).ConfigureAwait(false);

                    if (provider.LicenseApprovedForEntry(Path, type, site.Id, false, siteCountsAsSeat))
                        yield return new SiteSourceEntry(provider, this.Path, site);
                }
                break;
            default:
                Log.WriteWarningMessage(LOGTAG, "MetaSourceEntryEnumerateUnknownType", null, $"Attempted to enumerate unknown meta entry type: {type}");
                break;
        }
        yield break;
    }
}
