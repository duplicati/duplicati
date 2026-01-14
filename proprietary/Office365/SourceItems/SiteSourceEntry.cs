// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class SiteSourceEntry(SourceProvider provider, string path, GraphSite site)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, site.Id)), null, null)
{
    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
        {
            { "o365:v", "1" },
            { "o365:Id", site.Id },
            { "o365:Type", SourceItemType.Site.ToString() },
            { "o365:Name", $"{site.DisplayName}{(site.SiteCollection?.PersonalSite == true ? " (Personal)" : "" )}" },
            { "o365:DisplayName", site.DisplayName },
            { "o365:WebUrl", site.WebUrl },
            { "o365:Hostname", site.SiteCollection?.Hostname },
            { "o365:PersonalSite", site.SiteCollection?.PersonalSite?.ToString() }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "metadata.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: (ct) => provider.SiteApi.GetSiteMetadataStreamAsync(site.Id, ct)
        );

        await foreach (var drive in provider.SiteApi.ListSiteDrivesAsync(site.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new DriveSourceEntry(provider, this.Path, drive);
        }
    }
}
