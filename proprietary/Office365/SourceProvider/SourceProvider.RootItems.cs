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
        /// Lists the users in the tenant, carrying only what is needed to decide whether an
        /// account holds a Microsoft 365 license and which personal site belongs to it.
        /// </summary>
        /// <remarks>
        /// The licensing state cannot be filtered server-side: a filter on
        /// <c>assignedLicenses</c> is an advanced directory query and requires a
        /// <c>ConsistencyLevel</c> request header, which the paged reader does not send. The
        /// caller therefore evaluates it, and the cost is proportional to the size of the tenant
        /// rather than to the number of unlicensed accounts. Only three properties are selected
        /// to keep the pages small. <c>$orderby</c> is deliberately omitted, as the result is
        /// only used to build a lookup set where the ordering is irrelevant.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// An asynchronous enumerable of the users, carrying only the id, the user principal name
        /// and the assigned licenses.
        /// </returns>
        internal IAsyncEnumerable<GraphUser> ListUserLicenseStatesAsync(CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');

            var url =
                $"{baseUrl}/v1.0/users" +
                $"?$select={Uri.EscapeDataString("id,userPrincipalName,assignedLicenses")}" +
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
