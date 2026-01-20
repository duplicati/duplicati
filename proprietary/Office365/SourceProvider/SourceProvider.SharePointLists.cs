// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text;
using System.Text.Json;

namespace Duplicati.Proprietary.Office365;

partial class SourceProvider
{
    internal SharePointListApiImpl SharePointListApi => new SharePointListApiImpl(_apiHelper);

    internal class SharePointListApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphList> ListListsAsync(string siteId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var select = GraphSelectBuilder.BuildSelect<GraphList>();
            var site = Uri.EscapeDataString(siteId);

            var url =
                $"{baseUrl}/v1.0/sites/{site}/lists" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphList>(url, ct);
        }

        internal IAsyncEnumerable<GraphListItem> ListListItemsAsync(string siteId, string listId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            // Exclude 'fields' from select because we expand it
            var select = GraphSelectBuilder.BuildSelect<GraphListItem>(["fields"]);
            var site = Uri.EscapeDataString(siteId);
            var list = Uri.EscapeDataString(listId);

            var url =
                $"{baseUrl}/v1.0/sites/{site}/lists/{list}/items" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$expand=fields" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphListItem>(url, ct);
        }

        internal async IAsyncEnumerable<GraphAttachment> ListListItemAttachmentsAsync(
            string siteWebUrl,
            string listId,
            string itemId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            // SharePoint REST: /_api/web/lists(guid'...')/items(<id>)/AttachmentFiles
            var webUrl = siteWebUrl.TrimEnd('/');
            var listGuid = listId.Trim('{', '}');

            var url =
                $"{webUrl}/_api/web/lists(guid'{Uri.EscapeDataString(listGuid)}')" +
                $"/items({Uri.EscapeDataString(itemId)})/AttachmentFiles";

            using var stream = await provider.GetGraphItemAsStreamAsync(
                url,
                "application/json",
                "odata=nometadata",
                ct).ConfigureAwait(false);

            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            JsonElement items;
            if (doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array)
            {
                items = v;
            }
            else if (doc.RootElement.TryGetProperty("d", out var d) &&
                     d.TryGetProperty("results", out var r) &&
                     r.ValueKind == JsonValueKind.Array)
            {
                items = r;
            }
            else
            {
                yield break;
            }

            foreach (var el in items.EnumerateArray())
            {
                if (!el.TryGetProperty("FileName", out var fn))
                    continue;

                var fileName = fn.GetString();
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                yield return new GraphAttachment
                {
                    // For SharePoint list-item attachments, the filename is the identifier
                    Id = fileName!,
                    Name = fileName
                };
            }
        }

        internal Task<Stream> GetAttachmentContentStreamAsync(
            string siteWebUrl,
            string listId,
            string itemId,
            string attachmentFileName,
            CancellationToken ct)
        {
            // SharePoint REST:
            // /_api/web/lists(guid'...')/items(<id>)/AttachmentFiles('<filename>')/$value
            var webUrl = siteWebUrl.TrimEnd('/');
            var listGuid = listId.Trim('{', '}');

            // Escape single quotes for OData
            var safeFileName = attachmentFileName.Replace("'", "''");

            var url =
                $"{webUrl}/_api/web/lists(guid'{Uri.EscapeDataString(listGuid)}')" +
                $"/items({Uri.EscapeDataString(itemId)})" +
                $"/AttachmentFiles('{Uri.EscapeDataString(safeFileName)}')/$value";

            return provider.GetGraphResponseAsRealStreamAsync(url, "application/octet-stream", ct);
        }

        internal async Task<GraphList> CreateListAsync(string siteId, string displayName, GraphListInfo? listInfo, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);
            var url = $"{baseUrl}/v1.0/sites/{site}/lists";

            var list = new GraphList
            {
                DisplayName = displayName,
                List = listInfo ?? new GraphListInfo { Template = "genericList" }
            };

            var json = JsonSerializer.Serialize(list);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            return await provider.PostGraphItemAsync<GraphList>(url, content, ct).ConfigureAwait(false);
        }

        internal async Task<GraphList?> GetListAsync(string siteId, string displayName, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);
            var select = GraphSelectBuilder.BuildSelect<GraphList>();

            // Filter by displayName
            var filter = $"displayName eq '{displayName.Replace("'", "''")}'";

            var url =
                $"{baseUrl}/v1.0/sites/{site}/lists" +
                $"?$filter={Uri.EscapeDataString(filter)}" +
                $"&$select={Uri.EscapeDataString(select)}";

            var lists = await provider.GetAllGraphItemsAsync<GraphList>(url, ct).ToListAsync(ct).ConfigureAwait(false);
            return lists.FirstOrDefault();
        }

        internal async Task<GraphListItem> CreateListItemAsync(string siteId, string listId, JsonElement fields, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);
            var list = Uri.EscapeDataString(listId);
            var url = $"{baseUrl}/v1.0/sites/{site}/lists/{list}/items";

            var item = new { fields };
            var json = JsonSerializer.Serialize(item);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            return await provider.PostGraphItemAsync<GraphListItem>(url, content, ct).ConfigureAwait(false);
        }
    }
}
