// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

partial class RestoreProvider
{
    internal SiteApiImpl SiteApi => new SiteApiImpl(_apiHelper);

    internal class SiteApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphDrive> ListSiteDrivesAsync(string siteId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var select = GraphSelectBuilder.BuildSelect<GraphDrive>();
            var site = Uri.EscapeDataString(siteId);

            var url =
                $"{baseUrl}/v1.0/sites/{site}/drives" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphDrive>(url, ct);
        }

        internal IAsyncEnumerable<GraphSite> ListSubsitesAsync(string siteId, CancellationToken ct)
            => provider.ListSubsitesAsync(siteId, ct);
    }

    /// <summary>
    /// Restores the site hierarchy by mapping each backed-up site (including subsites)
    /// to a corresponding site in the target tenant. Subsites are matched by display name;
    /// Microsoft Graph does not support creating subsites, so unmatched subsites are skipped.
    /// </summary>
    private async Task RestoreSites(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        if (RestoreTarget.Type != SourceItemType.Site)
            return;

        var siteEntries = GetMetadataByType(SourceItemType.Site);
        if (siteEntries.Count == 0)
            return;

        var rootTargetSiteId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        if (string.IsNullOrWhiteSpace(rootTargetSiteId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreSitesMissingTargetId", null, "Target site ID is missing, cannot map site hierarchy.");
            return;
        }

        static string Normalize(string path)
            => Util.AppendDirSeparator(path.TrimEnd(Path.DirectorySeparatorChar));

        static string ParentOf(string path)
            => Util.AppendDirSeparator(Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)) ?? "");

        static int Depth(string path)
            => path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;

        // Normalize all site paths up-front so we can reason about the hierarchy.
        var normalizedEntries = siteEntries
            .Select(kv => new KeyValuePair<string, Dictionary<string, string?>>(Normalize(kv.Key), kv.Value))
            .ToList();

        // Set of all backed-up site paths, used to decide whether a site's parent is itself a site.
        var siteEntryPaths = new HashSet<string>(normalizedEntries.Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);

        // The true root is the shallowest site entry; guard against multiple top-level sites.
        var minDepth = normalizedEntries.Min(kv => Depth(kv.Key));

        // Sort by path depth so parents are processed before children.
        var sortedSites = normalizedEntries
            .OrderBy(kv => Depth(kv.Key))
            .ToList();

        var rootMapped = false;

        foreach (var siteEntry in sortedSites)
        {
            if (cancel.IsCancellationRequested)
                break;

            var originalPath = siteEntry.Key;
            var metadata = siteEntry.Value;
            var displayName = metadata.GetValueOrDefault("o365:DisplayName")
                ?? metadata.GetValueOrDefault("o365:Name")
                ?? metadata.GetValueOrDefault("o365:Id");

            _metadata.TryRemove(originalPath, out _);

            var parentPath = ParentOf(originalPath);
            var parentIsSite = !string.IsNullOrEmpty(parentPath) && siteEntryPaths.Contains(parentPath);

            // A site is a root when it sits at the shallowest depth and its parent is not
            // itself a backed-up site. Everything else is treated as a subsite.
            var isRoot = Depth(originalPath) == minDepth && !parentIsSite;

            if (isRoot)
            {
                if (rootMapped)
                {
                    // Multiple top-level sites cannot all map to a single restore target;
                    // mapping them together would collide their contents in one site.
                    _skippedSitePaths.Add(originalPath);
                    Log.WriteWarningMessage(LOGTAG, "RestoreSitesMultipleRoots", null, $"Multiple top-level sites found in the backup, but the restore target is a single site. Contents under '{originalPath}' will be skipped; restore each site separately.");
                    continue;
                }

                _restoredSiteMap[originalPath] = rootTargetSiteId;
                rootMapped = true;
                continue;
            }

            // Subsite handling: resolve the parent's mapped target site. If the parent could
            // not be mapped (missing or skipped), this subsite cannot be placed either.
            if (!_restoredSiteMap.TryGetValue(parentPath, out var parentTargetSiteId)
                || string.IsNullOrWhiteSpace(parentTargetSiteId))
            {
                _skippedSitePaths.Add(originalPath);
                Log.WriteWarningMessage(LOGTAG, "RestoreSubsiteParentUnmapped", null, $"Parent site for subsite '{originalPath}' was not mapped to a target site; contents under it will be skipped.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                _skippedSitePaths.Add(originalPath);
                Log.WriteWarningMessage(LOGTAG, "RestoreSubsiteMissingName", null, $"Missing display name for subsite {originalPath}, skipping.");
                continue;
            }

            // Find a matching subsite in the target tenant by display name.
            GraphSite? match = null;
            var enumerateFailed = false;
            try
            {
                await foreach (var sub in SiteApi.ListSubsitesAsync(parentTargetSiteId, cancel).ConfigureAwait(false))
                {
                    if (string.Equals(sub.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        match = sub;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                enumerateFailed = true;
                Log.WriteWarningMessage(LOGTAG, "RestoreSubsiteEnumerateFailed", ex, $"Failed to enumerate subsites under target site '{parentTargetSiteId}' for source subsite {originalPath}.");
            }

            if (match != null && !string.IsNullOrWhiteSpace(match.Id))
            {
                _restoredSiteMap[originalPath] = match.Id;
            }
            else
            {
                _skippedSitePaths.Add(originalPath);
                if (!enumerateFailed)
                    Log.WriteWarningMessage(LOGTAG, "RestoreSubsiteNoMatch", null, $"Could not find matching subsite for '{displayName}' under target site. Creating new subsites is not supported by the Graph API; contents under {originalPath} will be skipped.");
            }
        }
    }

    /// <summary>
    /// Resolves the target site ID for a given source path by walking up the path
    /// to find the nearest mapped site ancestor. Returns <c>null</c> when the nearest
    /// site ancestor was explicitly skipped (e.g., an unmatched subsite), so that the
    /// caller skips the content rather than misplacing it into an ancestor site.
    /// </summary>
    internal string? GetTargetSiteIdForPath(string sourcePath)
    {
        var current = Util.AppendDirSeparator(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
        while (!string.IsNullOrEmpty(current))
        {
            if (_restoredSiteMap.TryGetValue(current, out var siteId))
                return siteId;

            // If the nearest site ancestor was skipped, the content belongs to a site that
            // does not exist in the target; do not fall back to a different ancestor/root.
            if (_skippedSitePaths.Contains(current))
                return null;

            var parent = Util.AppendDirSeparator(
                Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar)) ?? "");

            if (string.IsNullOrEmpty(parent) || parent == current)
                break;

            current = parent;
        }

        // Fallback to the restore target site when restoring to a site and no mapping
        // was found (e.g., a backup without explicit Site metadata entries).
        if (RestoreTarget?.Type == SourceItemType.Site)
            return RestoreTarget.Metadata.GetValueOrDefault("o365:Id");

        return null;
    }

    private async Task RestoreSiteDrives(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        // Only proceed if we are restoring to a Site
        if (RestoreTarget.Type != SourceItemType.Site)
            return;

        var driveSources = GetMetadataByType(SourceItemType.Drive);
        if (driveSources.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(RestoreTarget.Metadata.GetValueOrDefault("o365:Id")))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreSiteDrivesMissingId", null, "Target site ID is missing, cannot restore site drives.");
            return;
        }

        // Cache of existing drives per target site ID to avoid re-fetching for sibling drives
        var drivesBySite = new Dictionary<string, List<GraphDrive>>(StringComparer.OrdinalIgnoreCase);

        foreach (var driveSource in driveSources)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var metadata = driveSource.Value;
                var sourceName = metadata.GetValueOrDefault("o365:Name");

                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    // Fallback to using the last part of the path if name is missing
                    sourceName = Path.GetFileName(driveSource.Key.TrimEnd(Path.DirectorySeparatorChar));
                }

                // GetTargetSiteIdForPath resolves the owning site (root or matched subsite) and
                // returns null when the drive belongs to a site that was skipped (e.g., an
                // unmatched subsite). In that case the drive must be skipped, not misplaced.
                var targetSiteId = GetTargetSiteIdForPath(driveSource.Key);
                if (string.IsNullOrWhiteSpace(targetSiteId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreSiteDrivesNoSite", null, $"Could not resolve target site for drive '{sourceName}' at {driveSource.Key}, skipping.");
                    continue;
                }

                if (!drivesBySite.TryGetValue(targetSiteId, out var targetDrives))
                {
                    targetDrives = new List<GraphDrive>();
                    try
                    {
                        await foreach (var drive in SiteApi.ListSiteDrivesAsync(targetSiteId, cancel))
                            targetDrives.Add(drive);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreSiteDrivesListFailed", ex, $"Failed to list drives on target site '{targetSiteId}'.");
                    }
                    drivesBySite[targetSiteId] = targetDrives;
                }

                // Try to match by name
                var match = targetDrives.FirstOrDefault(d =>
                    string.Equals(d.Name, sourceName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    _restoredDriveMap[driveSource.Key] = match.Id;
                }
                else
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreSiteDrivesNoMatch", null, $"Could not find matching drive for '{sourceName}' in target site. Creating new document libraries is not yet supported.");
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreSiteDrivesFailed", ex, $"Failed to restore site drive {driveSource.Key}");
            }
        }
    }

}
