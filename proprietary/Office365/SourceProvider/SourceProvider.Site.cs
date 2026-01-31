// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365;

partial class SourceProvider
{
    internal SiteApiImpl SiteApi => new SiteApiImpl(_apiHelper);

    internal class SiteApiImpl(APIHelper provider)
    {
        internal Task<Stream> GetSiteMetadataStreamAsync(string siteId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);

            var select = "id,name,displayName,webUrl,createdDateTime,description,siteCollection,root";
            var url =
                $"{baseUrl}/v1.0/sites/{site}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

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

        internal async Task<GraphDrive> GetSitePrimaryDriveAsync(string siteId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);

            var select = GraphSelectBuilder.BuildSelect<GraphDrive>();
            var url =
                $"{baseUrl}/v1.0/sites/{site}/drive" +
                $"?$select={Uri.EscapeDataString(select)}";

            var drive = await provider.GetGraphItemAsync<GraphDrive>(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(drive.Id))
                throw new UserInformationException("Failed to read site's primary drive.", nameof(SourceProvider));

            return drive;
        }
    }
}
