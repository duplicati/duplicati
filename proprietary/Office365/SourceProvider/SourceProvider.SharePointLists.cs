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

        // Case-insensitive set of fields that SharePoint treats as read-only/system
        private static readonly IReadOnlySet<string> ListItemBlockedFields =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "LinkTitle",
                "LinkTitleNoMenu",
                "ID",
                "Edit",
                "GUID",
                "Created",
                "Modified",
                "Author",
                "AuthorLookupId",
                "Editor",
                "EditorLookupId",
                "FileLeafRef",
                "FileRef",
                "FileDirRef",
                "EncodedAbsUrl",
                "ServerUrl",
                "ServerRelativeUrl",
                "Attachments",
                "ItemChildCount",
                "FolderChildCount",
                "ContentType",
                "_UIVersionString",
                "_ComplianceFlags",
                "_ComplianceTag"
            };

        internal async Task<IReadOnlySet<string>> GetBlockedListItemFieldNamesAsync(
            string siteId,
            string listId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);
            var list = Uri.EscapeDataString(listId);

            var url =
                $"{baseUrl}/v1.0/sites/{site}/lists/{list}/columns" +
                "?$select=name,readOnly,hidden,isSealed";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization =
                    await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            blocked.UnionWith(ListItemBlockedFields);

            if (doc.RootElement.TryGetProperty("value", out var columns))
            {
                foreach (var col in columns.EnumerateArray())
                {
                    var name = col.GetProperty("name").GetString();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var readOnly = col.TryGetProperty("readOnly", out var ro) && ro.GetBoolean();
                    var hidden = col.TryGetProperty("hidden", out var hid) && hid.GetBoolean();
                    var sealedCol = col.TryGetProperty("isSealed", out var seal) && seal.GetBoolean();

                    if (readOnly) // || hidden || sealedCol)
                        blocked.Add(name);
                }
            }

            return blocked;
        }

        internal async Task<GraphListItem?> CreateListItemAsync(
            string siteId,
            string listId,
            JsonElement fields,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);
            var list = Uri.EscapeDataString(listId);
            var url = $"{baseUrl}/v1.0/sites/{site}/lists/{list}/items";

            var blockedFields = await GetBlockedListItemFieldNamesAsync(siteId, listId, ct).ConfigureAwait(false);

            var any = false;
            using var ms = new MemoryStream();
            using (var jw = new Utf8JsonWriter(ms))
            {
                jw.WriteStartObject();
                jw.WritePropertyName("fields");
                jw.WriteStartObject();

                if (fields.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in fields.EnumerateObject())
                    {
                        if (blockedFields.Contains(prop.Name))
                            continue;
                        any = true;
                        jw.WritePropertyName(prop.Name);
                        prop.Value.WriteTo(jw);
                    }
                }

                jw.WriteEndObject();
                jw.WriteEndObject();
            }

            if (!any)
                return null;

            ms.Position = 0;
            return await provider
                .PostGraphItemStreamAsync<GraphListItem>(url, ms, "application/json", ct)
                .ConfigureAwait(false);
        }

        internal async Task<bool> IsDocumentLibraryAsync(string siteId, string listId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);
            var list = Uri.EscapeDataString(listId);

            // list.template is available under the "list" facet
            var url = $"{baseUrl}/v1.0/sites/{site}/lists/{list}?$select=list";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("list", out var listFacet) || listFacet.ValueKind != JsonValueKind.Object)
                return false;

            if (!listFacet.TryGetProperty("template", out var templateProp) || templateProp.ValueKind != JsonValueKind.String)
                return false;

            var template = templateProp.GetString();
            return string.Equals(template, "documentLibrary", StringComparison.OrdinalIgnoreCase);
        }

        internal async Task<string?> GetDriveIdForListAsync(string siteId, string listId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);
            var list = Uri.EscapeDataString(listId);

            // Works only for document libraries; non-libraries typically return 404.
            var url = $"{baseUrl}/v1.0/sites/{site}/lists/{list}/drive?$select=id";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                return idProp.GetString();

            return null;
        }

        internal Task<Stream> GetItemContentStreamAsync(string siteId, string listId, string itemId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var site = Uri.EscapeDataString(siteId);
            var list = Uri.EscapeDataString(listId);
            var item = Uri.EscapeDataString(itemId);

            var url = $"{baseUrl}/v1.0/sites/{site}/lists/{list}/items/{item}/driveItem/content";
            return provider.GetGraphResponseAsRealStreamAsync(url, "application/octet-stream", ct);
        }
    }
}
