// Copyright (c) 2026 Duplicati Inc. All rights reserved.

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

        var targetSiteId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        if (string.IsNullOrWhiteSpace(targetSiteId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreSiteDrivesMissingId", null, "Target site ID is missing, cannot restore site drives.");
            return;
        }

        try
        {
            // List existing drives on the target site
            var targetDrives = new List<GraphDrive>();
            await foreach (var drive in SiteApi.ListSiteDrivesAsync(targetSiteId, cancel))
            {
                targetDrives.Add(drive);
            }

            foreach (var driveSource in driveSources)
            {
                var metadata = driveSource.Value;
                var sourceName = metadata.GetValueOrDefault("o365:Name");

                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    // Fallback to using the last part of the path if name is missing
                    sourceName = Path.GetFileName(driveSource.Key.TrimEnd(Path.DirectorySeparatorChar));
                }

                // Try to match by name
                var match = targetDrives.FirstOrDefault(d =>
                    string.Equals(d.Name, sourceName, StringComparison.OrdinalIgnoreCase));

                // Also try to match by "Documents" if source is "Documents" but target might be named differently in URL but same display name?
                // Or if source is "Shared Documents" and target is "Documents" (common in SharePoint).
                // But let's stick to exact name match for now.

                if (match != null)
                {
                    _restoredDriveMap[driveSource.Key] = match.Id;
                }
                else
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreSiteDrivesNoMatch", null, $"Could not find matching drive for '{sourceName}' in target site. Creating new document libraries is not yet supported.");
                }
            }
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "RestoreSiteDrivesFailed", ex, $"Failed to restore site drives");
        }
    }

}
