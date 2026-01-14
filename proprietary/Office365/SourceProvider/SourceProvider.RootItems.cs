// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365;

partial class SourceProvider
{
    internal RootApiImpl RootApi => new RootApiImpl(_apiHelper);

    internal class RootApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphUser> ListAllUsersAsync(CancellationToken ct)
        {
            // GET /users with paging via @odata.nextLink
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var select = GraphSelectBuilder.BuildSelect<GraphUser>();

            var url =
                $"{baseUrl}/v1.0/users" +
                $"?$select={Uri.EscapeDataString(select)}" +
                   $"&$top={GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphUser>(url, ct);
        }

        internal IAsyncEnumerable<GraphGroup> ListAllGroupsAsync(CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var select = GraphSelectBuilder.BuildSelect<GraphGroup>();
            var url =
                $"{baseUrl}/v1.0/groups" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphGroup>(url, ct);
        }

        internal IAsyncEnumerable<GraphGroup> ListUnifiedGroupsAsync(CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var filter = "groupTypes/any(c:c eq 'Unified')";
            var select = GraphSelectBuilder.BuildSelect<GraphGroup>();

            var url =
                $"{baseUrl}/v1.0/groups" +
                $"?$filter={Uri.EscapeDataString(filter)}" +
                $"&$select={Uri.EscapeDataString(select)}" +
                $"&$top={GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphGroup>(url, ct);
        }

        internal IAsyncEnumerable<GraphSite> ListAllSitesAsync(CancellationToken ct)
        {
            // Tenant-wide enumeration: /sites/getAllSites
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var select = GraphSelectBuilder.BuildSelect<GraphSite>();

            var url =
                $"{baseUrl}/v1.0/sites/getAllSites" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphSite>(url, ct);
        }
    }
}
