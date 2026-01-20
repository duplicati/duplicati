// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal enum Office365MetaType
{
    // Tenant directory / discovery roots
    Users,
    Groups,
    Sites,

    // // SharePoint structured content
    // Lists,               // SharePoint lists
    // ListItems,           // SharePoint list rows/items

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
                    _ => SourceItemType.MetaRoot.ToString()
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
                await foreach (var user in provider.RootApi.ListAllUsersAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    if (!provider.LicenseApprovedForEntry(Path, type, user.Id))
                        yield break;

                    yield return new UserSourceEntry(provider, Path, user);
                }
                break;
            case Office365MetaType.Groups:
                await foreach (var group in provider.RootApi.ListAllGroupsAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    if (!provider.LicenseApprovedForEntry(Path, type, group.Id))
                        yield break;

                    yield return new GroupSourceEntry(provider, this.Path, group);
                }
                break;
            case Office365MetaType.Sites:
                await foreach (var site in provider.RootApi.ListAllSitesAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    if (!provider.LicenseApprovedForEntry(Path, type, site.Id))
                        yield break;

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
