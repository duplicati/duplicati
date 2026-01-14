// Copyright (c) 2026 Duplicati Inc. All rights reserved.

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

            return provider.GetGraphAsStreamAsync(url, "application/json", ct);
        }

        internal IAsyncEnumerable<GraphDrive> ListSiteDrivesAsync(string siteId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var select = GraphSelectBuilder.BuildSelect<GraphDrive>();
            var site = Uri.EscapeDataString(siteId);

            var url =
                $"{baseUrl}/v1.0/sites/{site}/drives" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphDrive>(url, ct);
        }
    }
}
