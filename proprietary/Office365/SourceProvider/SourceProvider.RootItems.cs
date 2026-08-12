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

            // Order by displayName to give the paged user feed a stable ordering, reducing
            // the chance that users are skipped or returned on more than one page.
            // displayName is one of the few user properties Graph can sort on without
            // advanced query parameters. The source entry still de-duplicates by user id
            // as a safety net.
            var url =
                $"{baseUrl}/v1.0/users" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$orderby={Uri.EscapeDataString("displayName")}" +
                $"&$top={APIHelper.BIG_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphUser>(url, ct);
        }

        /// <summary>
        /// Lists the users in the tenant whose account is disabled.
        /// </summary>
        /// <remarks>
        /// The filtering is done server-side and only the user principal name is selected, so
        /// the cost is proportional to the number of disabled users rather than to the size of
        /// the tenant. <c>$orderby</c> is deliberately omitted: combining it with a filter on
        /// <c>accountEnabled</c> requires an advanced directory query, and the result is only
        /// used to build a lookup set where the ordering is irrelevant.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// An asynchronous enumerable of the disabled users, carrying only the id and the user
        /// principal name.
        /// </returns>
        internal IAsyncEnumerable<GraphUser> ListDisabledUsersAsync(CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');

            var url =
                $"{baseUrl}/v1.0/users" +
                $"?$filter={Uri.EscapeDataString("accountEnabled eq false")}" +
                $"&$select={Uri.EscapeDataString("id,userPrincipalName")}" +
                $"&$top={APIHelper.BIG_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphUser>(url, ct);
        }

        internal IAsyncEnumerable<GraphGroup> ListAllGroupsAsync(CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var select = GraphSelectBuilder.BuildSelect<GraphGroup>();

            // Order by displayName to give the paged group feed a stable ordering, reducing
            // the chance that groups are skipped or returned on more than one page.
            // displayName is the only group property Graph can sort on without advanced
            // query parameters. The source entry still de-duplicates by group id as a
            // safety net.
            var url =
                $"{baseUrl}/v1.0/groups" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$orderby={Uri.EscapeDataString("displayName")}" +
                $"&$top={APIHelper.BIG_PAGE_SIZE}";

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
                $"&$top={APIHelper.BIG_PAGE_SIZE}";

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
                $"&$top={APIHelper.BIG_PAGE_SIZE}";

            // getAllSites does not support $orderby, so the alphabetical ordering that is
            // presented to the user has to be applied on the client.
            return APIHelper.OrderSitesByNameAsync(provider.GetAllGraphItemsAsync<GraphSite>(url, ct), ct);
        }
    }
}
