// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

partial class RestoreProvider
{
    internal ContactApiImpl ContactApi => new ContactApiImpl(_apiHelper);
    private ContactRestoreHelper? _contactRestoreHelper = null;
    internal ContactRestoreHelper ContactRestore => _contactRestoreHelper ??= new ContactRestoreHelper(this);

    internal class ContactApiImpl(APIHelper provider)
    {
        public async Task<GraphContact> RestoreContactToFolderAsync(
            string userId,
            string targetFolderId,
            Stream contactJsonStream,
            CancellationToken cancellationToken)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var folder = Uri.EscapeDataString(targetFolderId);

            // POST /users/{id}/contactFolders/{id}/contacts
            var url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{folder}/contacts";

            async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);
                contactJsonStream.Position = 0;
                req.Content = new StreamContent(contactJsonStream);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                cancellationToken).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);

            using var respStream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphContact>(respStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the created contact id.");

            return created;
        }

        public async Task<GraphContactFolder> CreateContactFolderAsync(
            string userId,
            string displayName,
            string? parentFolderId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);

            string url;
            if (string.IsNullOrWhiteSpace(parentFolderId))
            {
                // POST /users/{id}/contactFolders
                url = $"{baseUrl}/v1.0/users/{user}/contactFolders";
            }
            else
            {
                // POST /users/{id}/contactFolders/{id}/childFolders
                var parent = Uri.EscapeDataString(parentFolderId);
                url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{parent}/childFolders";
            }

            var body = new { displayName = displayName };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
                => new HttpRequestMessage(HttpMethod.Post, new Uri(url))
                {
                    Headers =
                    {
                        Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false)
                    },
                    Content = JsonContent.Create(body)
                };

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphContactFolder>(respStream, cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the created contact folder id.");

            return created;
        }

        public async Task RestoreContactPhotoAsync(
            string userId,
            string contactId,
            string? contactFolderId,
            Stream photoStream,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var contact = Uri.EscapeDataString(contactId);

            string url;
            if (string.IsNullOrWhiteSpace(contactFolderId))
            {
                // PUT /users/{id}/contacts/{id}/photo/$value
                url = $"{baseUrl}/v1.0/users/{user}/contacts/{contact}/photo/$value";
            }
            else
            {
                // PUT /users/{id}/contactFolders/{id}/contacts/{id}/photo/$value
                var folder = Uri.EscapeDataString(contactFolderId);
                url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{folder}/contacts/{contact}/photo/$value";
            }

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Put, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                photoStream.Position = 0;
                req.Content = new StreamContent(photoStream);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg"); // Assuming JPEG, but Graph usually detects or accepts generic
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
        }

        public async Task<GraphContactFolder?> GetContactFolderByNameAsync(
            string userId,
            string displayName,
            string? parentFolderId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var filterName = displayName.Replace("'", "''");

            string url;
            if (string.IsNullOrWhiteSpace(parentFolderId))
            {
                url = $"{baseUrl}/v1.0/users/{user}/contactFolders?$filter=displayName eq '{filterName}'&$top=1";
            }
            else
            {
                var parent = Uri.EscapeDataString(parentFolderId);
                url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{parent}/childFolders?$filter=displayName eq '{filterName}'&$top=1";
            }

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GraphPage<GraphContactFolder>>(stream, cancellationToken: ct).ConfigureAwait(false);

            return result?.Value?.FirstOrDefault();
        }

        public async Task<List<GraphContact>> FindContactsAsync(
                    string userId,
                    string folderId,
                    string? email,
                    string? displayName,
                    CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var folder = Uri.EscapeDataString(folderId);

            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(email))
            {
                filters.Add($"emailAddresses/any(a:a/address eq '{email}')");
            }
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                filters.Add($"displayName eq '{displayName.Replace("'", "''")}'");
            }

            if (filters.Count == 0) return [];

            var filter = string.Join(" or ", filters);
            var url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{folder}/contacts?$filter={filter}";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<GraphPage<GraphContact>>(stream, cancellationToken: ct).ConfigureAwait(false);
            return page?.Value ?? [];
        }

        public async Task<List<GraphContactGroup>> FindContactGroupsAsync(
            string userIdOrUpn,
            string folderId,
            string displayName,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var folder = Uri.EscapeDataString(folderId);

            // OData string literal escaping
            var filterName = displayName.Replace("'", "''");
            var filter = $"displayName eq '{filterName}'";

            // Must include @odata.type to distinguish contactGroup vs contact
            var select = GraphSelectBuilder.BuildSelect<GraphContactGroup>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/contactFolders/{folder}/contacts" +
                $"?$filter={Uri.EscapeDataString(filter)}" +
                $"&$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<GraphPage<GraphContact>>(stream, cancellationToken: ct).ConfigureAwait(false);

            var results = new List<GraphContactGroup>();
            if (page?.Value == null)
                return results;

            foreach (var item in page.Value)
            {
                results.Add(new GraphContactGroup
                {
                    Id = item.Id,
                    DisplayName = item.DisplayName,
                    ParentFolderId = item.ParentFolderId
                });
            }

            return results;
        }

        public async Task<GraphContactGroup> CreateContactGroupAsync(
            string userIdOrUpn,
            string displayName,
            string folderId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var folder = Uri.EscapeDataString(folderId);

            var url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{folder}/contacts";

            var body = new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.contactGroup",
                ["displayName"] = displayName
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(body);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphContactGroup>(respStream, cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the created contact group id.");

            return created;
        }

        public async Task AddContactGroupMemberAsync(
            string userIdOrUpn,
            string folderId,
            string contactGroupId,
            GraphEmailAddress member,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var folder = Uri.EscapeDataString(folderId);
            var group = Uri.EscapeDataString(contactGroupId);

            var url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{folder}/contacts/{group}";

            var body = new
            {
                members = new[]
                {
                    new { emailAddress = new { name = member.Name, address = member.Address } }
                }
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Patch, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(body);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
        }
    }

    internal class ContactRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetUserId = null;
        private string? _targetFolderId = null;
        private bool _hasLoadedTargetInfo = false;

        public async Task<(string? UserId, string? FolderId)> GetUserIdAndContactFolderTarget(CancellationToken cancel)
        {
            if (_hasLoadedTargetInfo)
            {
                if (string.IsNullOrWhiteSpace(_targetUserId) || string.IsNullOrWhiteSpace(_targetFolderId))
                    return (null, null);
                return (_targetUserId!, _targetFolderId!);
            }

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            if (target.Type == SourceItemType.User)
            {
                _targetUserId = target.Metadata["o365:Id"]!;
                _targetFolderId = await GetDefaultRestoreTargetContactFolder(_targetUserId, cancel);
            }
            else if (target.Type == SourceItemType.UserContacts)
            {
                _targetUserId = target.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();

                if (_targetUserId != null)
                    _targetFolderId = await GetDefaultRestoreTargetContactFolder(_targetUserId, cancel);
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreContactsInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring contacts.");
            }

            _hasLoadedTargetInfo = true;

            if (string.IsNullOrWhiteSpace(_targetUserId) || string.IsNullOrWhiteSpace(_targetFolderId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreContactsMissingIds", null, $"Missing target userId or folderId for restoring contacts.");
                return (null, null);
            }

            return (_targetUserId, _targetFolderId);
        }

        private async Task<string> GetDefaultRestoreTargetContactFolder(string userId, CancellationToken cancel)
        {
            const string RESTORED_FOLDER_NAME = "Restored";

            var existing = await Provider.ContactApi.GetContactFolderByNameAsync(userId, RESTORED_FOLDER_NAME, null, cancel);
            if (existing != null)
                return existing.Id;

            var created = await Provider.ContactApi.CreateContactFolderAsync(userId, RESTORED_FOLDER_NAME, null, cancel);
            return created.Id;
        }
    }
}
