// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class SiteSourceEntry(SourceProvider provider, string parentPath, GraphSite site)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, site.Id)), null, null)
{
    private static readonly string LOGTAG = Log.LogTagFromType<SiteSourceEntry>();

    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => new Dictionary<string, string?>()
        {
            { "o365:v", "1" },
            { "o365:Id", site.Id },
            { "o365:Type", SourceItemType.Site.ToString() },
            { "o365:Name", $"{site.DisplayName}{(site.IsPersonalSite == true ? " (Personal)" : "" )}" },
            { "o365:DisplayName", site.DisplayName },
            { "o365:WebUrl", site.WebUrl },
            { "o365:Hostname", site.SiteCollection?.Hostname },
            { "o365:PersonalSite", site.IsPersonalSite?.ToString() },
            // Only the user interface consumes the classification, and resolving it may require
            // the unlicensed-user lookup, so a backup does not pay for it.
            { "o365:Classification", provider.EnumerationMode ? await GetSiteClassificationAsync(cancellationToken).ConfigureAwait(false) : null }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// Gets the classification string for treeview display, using the full classification
    /// (which separates personal sites of unlicensed users) and falling back to the
    /// site-object-only classification if it cannot be resolved.
    /// </summary>
    /// <remarks>
    /// The full classification may require the unlicensed-user lookup, which costs a single
    /// listing of the tenant's accounts and is then shared by every other site. It is
    /// resolved here rather than while enumerating so that a listing that displays no personal
    /// site does not pay for it at all.
    /// </remarks>
    private async Task<string> GetSiteClassificationAsync(CancellationToken cancellationToken)
    {
        var cached = provider.TryGetCachedSiteCategory(site.Id);
        if (cached != null)
            return cached.Value.ToString();

        try
        {
            var category = await provider.ClassifySiteAsync(site, cancellationToken).ConfigureAwait(false);
            return category.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Metadata generation must not fail over a classification lookup.
            Log.WriteVerboseMessage(LOGTAG, "SiteClassificationLookupFailed", ex, $"Failed to resolve the classification for site '{site.Id}'; falling back to the site classification.");
            return SourceProvider.ClassifySite(site).ToString();
        }
    }

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Personal sites belonging to a user account without an assigned Microsoft 365 license
        // do not consume a seat, the same way the account itself does not.
        var countsAsSeat = await provider.SiteCountsAsSeatAsync(site, cancellationToken).ConfigureAwait(false);

        if (!provider.LicenseApprovedForEntry(parentPath, Office365MetaType.Sites, site.Id, true, countsAsSeat))
            yield break;

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

        await foreach (var list in provider.SharePointListApi.ListListsAsync(site.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new SharePointListSourceEntry(provider, this.Path, site, list);
        }

        await foreach (var subsite in provider.SiteApi.ListSubsitesAsync(site.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            // Guard against the API echoing the parent site itself, which would cause infinite recursion.
            if (string.Equals(subsite.Id, site.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new SiteSourceEntry(provider, this.Path, subsite);
        }
    }
}
