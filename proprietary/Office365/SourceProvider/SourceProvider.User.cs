// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365;

partial class SourceProvider
{
    internal UserEmailApiImpl UserEmailApi => new UserEmailApiImpl(_apiHelper);

    internal class UserEmailApiImpl(APIHelper provider)
    {
        /// <summary>
        /// Lists all messages in the user's mailbox (across all folders) using paging via @odata.nextLink.
        /// Requires Microsoft Graph application permission Mail.Read (or Mail.ReadBasic.All for limited fields).
        /// </summary>
        internal IAsyncEnumerable<GraphMessage> ListAllEmailsAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var select = GraphSelectBuilder.BuildSelect<GraphMessage>();

            // Consider adding $filter=receivedDateTime ge ... for incremental runs.
            var url =
                $"{baseUrl}/v1.0/users/{user}/messages" +
                $"?$select={Uri.EscapeDataString(select)}" +
                "&$orderby=receivedDateTime asc" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphMessage>(url, ct);
        }

        /// <summary>
        /// Lists all messages in a specific mail folder.
        /// </summary>
        internal IAsyncEnumerable<GraphMessage> ListAllEmailsInFolderAsync(
            string userIdOrUpn,
            string folderId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var folder = Uri.EscapeDataString(folderId);
            var select = GraphSelectBuilder.BuildSelect<GraphMessage>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/mailFolders/{folder}/messages" +
                $"?$select={Uri.EscapeDataString(select)}" +
                "&$orderby=receivedDateTime asc" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphMessage>(url, ct);
        }

        internal async Task<GraphMailFolder> GetMailRootFolderAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var select = GraphSelectBuilder.BuildSelect<GraphMailFolder>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/mailFolders/msgfolderroot" +
                $"?$select={Uri.EscapeDataString(select)}";

            var folder = await provider.GetGraphItemAsync<GraphMailFolder>(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(folder.Id))
                throw new UserInformationException("Failed to read root mail folder.", nameof(SourceProvider));

            return folder;
        }

        /// <summary>
        /// Enumerates mail folders under a given folder. Use folderId = "msgfolderroot" to list top-level folders.
        /// For a full tree, call this recursively for each returned folder.
        /// </summary>
        internal IAsyncEnumerable<GraphMailFolder> ListMailChildFoldersAsync(string userIdOrUpn, string folderId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var folder = Uri.EscapeDataString(folderId);
            var select = GraphSelectBuilder.BuildSelect<GraphMailFolder>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/mailFolders/{folder}/childFolders" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphMailFolder>(url, ct);
        }

        internal Task<Stream> GetEmailContentStreamAsync(string userIdOrUpn, string messageId, CancellationToken ct)
        {
            // MIME (RFC 822) content of the message
            // Graph returns message/rfc822
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var msg = Uri.EscapeDataString(messageId);

            var url = $"{baseUrl}/v1.0/users/{user}/messages/{msg}/$value";
            return provider.GetGraphResponseAsRealStreamAsync(url, "message/rfc822", ct);
        }

        internal Task<Stream> GetEmailMetadataStreamAsync(string userIdOrUpn, string messageId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var msg = Uri.EscapeDataString(messageId);

            // Mailbox-specific state that is NOT preserved in RFC822
            var select =
                "id,parentFolderId,isRead,importance,categories,flag," +
                "conversationId,conversationIndex,internetMessageId," +
                "receivedDateTime,sentDateTime,createdDateTime,lastModifiedDateTime," +
                "from,toRecipients,ccRecipients,bccRecipients,replyTo,sender,subject,hasAttachments";

            var url = $"{baseUrl}/v1.0/users/{user}/messages/{msg}?$select={Uri.EscapeDataString(select)}";
            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal IAsyncEnumerable<GraphMessageRule> ListMessageRulesAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var select = GraphSelectBuilder.BuildSelect<GraphMessageRule>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/mailFolders/inbox/messageRules" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphMessageRule>(url, ct);
        }

        internal Task<Stream> GetMessageRuleStreamAsync(string userIdOrUpn, string ruleId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var rule = Uri.EscapeDataString(ruleId);
            var select = GraphSelectBuilder.BuildSelect<GraphMessageRule>();

            var url = $"{baseUrl}/v1.0/users/{user}/mailFolders/inbox/messageRules/{rule}?$select={Uri.EscapeDataString(select)}";
            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal Task<Stream> GetMailboxSettingsStreamAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            var select = GraphSelectBuilder.BuildSelect<GraphMailboxSettings>();
            var url = $"{baseUrl}/v1.0/users/{user}/mailboxSettings?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal async Task UpdateMailboxSettingsAsync(string userIdOrUpn, GraphMailboxSettings settings, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var url = $"{baseUrl}/v1.0/users/{user}/mailboxSettings";

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            await provider.PatchGraphItemAsync(url, content, ct);
        }

        internal async Task CreateMessageRuleAsync(string userIdOrUpn, GraphMessageRule rule, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var url = $"{baseUrl}/v1.0/users/{user}/mailFolders/inbox/messageRules";

            // Remove ID and read-only properties before creating
            rule.Id = "";
            rule.IsReadOnly = null;
            rule.HasError = null;

            var json = JsonSerializer.Serialize(rule, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            await provider.PostGraphItemAsync<GraphMessageRule>(url, content, ct);
        }
    }

    internal ContactsApiImpl ContactsApi => new ContactsApiImpl(_apiHelper);
    internal class ContactsApiImpl(APIHelper provider)
    {
        /// <summary>
        /// Enumerates all contacts in the user's default Contacts container.
        /// For contact folders too, enumerate /contactFolders and then /contactFolders/{id}/contacts.
        /// Requires Microsoft Graph application permission Contacts.Read (or Contacts.ReadWrite).
        /// </summary>
        internal IAsyncEnumerable<GraphContact> ListAllContactsAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var select = GraphSelectBuilder.BuildSelect<GraphContact>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/contacts" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphContact>(url, ct);
        }

        internal IAsyncEnumerable<GraphContactFolder> ListContactFoldersAsync(string userIdOrUpn, string? parentFolderId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var select = GraphSelectBuilder.BuildSelect<GraphContactFolder>();

            string url;
            if (string.IsNullOrWhiteSpace(parentFolderId))
            {
                url = $"{baseUrl}/v1.0/users/{user}/contactFolders" +
                      $"?$select={Uri.EscapeDataString(select)}" +
                      $"&$top={APIHelper.GENERAL_PAGE_SIZE}";
            }
            else
            {
                var parent = Uri.EscapeDataString(parentFolderId);
                url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{parent}/childFolders" +
                      $"?$select={Uri.EscapeDataString(select)}" +
                      $"&$top={APIHelper.GENERAL_PAGE_SIZE}";
            }

            return provider.GetAllGraphItemsAsync<GraphContactFolder>(url, ct);
        }

        internal IAsyncEnumerable<GraphContact> ListContactsInFolderAsync(string userIdOrUpn, string folderId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var folder = Uri.EscapeDataString(folderId);
            var select = GraphSelectBuilder.BuildSelect<GraphContact>();

            var url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{folder}/contacts" +
                      $"?$select={Uri.EscapeDataString(select)}" +
                      $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphContact>(url, ct);
        }

        internal Task<Stream> GetContactPhotoStreamAsync(string userIdOrUpn, string contactId, string? folderId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var contact = Uri.EscapeDataString(contactId);

            string url;
            if (string.IsNullOrWhiteSpace(folderId))
            {
                url = $"{baseUrl}/v1.0/users/{user}/contacts/{contact}/photo/$value";
            }
            else
            {
                var folder = Uri.EscapeDataString(folderId);
                url = $"{baseUrl}/v1.0/users/{user}/contactFolders/{folder}/contacts/{contact}/photo/$value";
            }

            return provider.GetGraphResponseAsRealStreamAsync(url, "application/octet-stream", ct);
        }

        internal IAsyncEnumerable<GraphRecipient> ListContactGroupMembersAsync(
            string userIdOrUpn,
            string contactGroupId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var group = Uri.EscapeDataString(contactGroupId);

            var select = GraphSelectBuilder.BuildSelect<GraphRecipient>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/contacts/{group}/members" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphRecipient>(url, ct);
        }

        internal IAsyncEnumerable<GraphRecipient> ListContactGroupMembersAsync(
            string userIdOrUpn,
            string contactFolderId,
            string contactGroupId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var folder = Uri.EscapeDataString(contactFolderId);
            var group = Uri.EscapeDataString(contactGroupId);

            var select = GraphSelectBuilder.BuildSelect<GraphRecipient>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/contactFolders/{folder}/contacts/{group}/members" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphRecipient>(url, ct);
        }

        internal async Task<bool> IsContactGroupAsync(string userIdOrUpn, string contactId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var id = Uri.EscapeDataString(contactId);

            var url = $"{baseUrl}/v1.0/users/{user}/contacts/{id}/members?$top=1";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode)
                return true;

            // Contacts will typically fail with 400/404 for the 'members' segment.
            if (resp.StatusCode == HttpStatusCode.BadRequest ||
                resp.StatusCode == HttpStatusCode.NotFound)
                return false;

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
            return false;
        }
    }

    internal TodoApiImpl TodoApi => new TodoApiImpl(_apiHelper);

    internal class TodoApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphTodoTaskList> ListUserTaskListsAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            var select = GraphSelectBuilder.BuildSelect<GraphTodoTaskList>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/todo/lists" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphTodoTaskList>(url, ct);
        }

        internal IAsyncEnumerable<GraphTodoTask> ListTaskListTasksAsync(string userIdOrUpn, string taskListId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var list = Uri.EscapeDataString(taskListId);

            var select = GraphSelectBuilder.BuildSelect<GraphTodoTask>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/todo/lists/{list}/tasks" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphTodoTask>(url, ct);
        }

        internal IAsyncEnumerable<GraphTodoChecklistItem> ListTaskChecklistItemsAsync(
            string userIdOrUpn,
            string taskListId,
            string taskId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var list = Uri.EscapeDataString(taskListId);
            var task = Uri.EscapeDataString(taskId);

            var select = GraphSelectBuilder.BuildSelect<GraphTodoChecklistItem>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/todo/lists/{list}/tasks/{task}/checklistItems" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphTodoChecklistItem>(url, ct);
        }

        internal IAsyncEnumerable<GraphTodoLinkedResource> ListTaskLinkedResourcesAsync(
            string userIdOrUpn,
            string taskListId,
            string taskId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var list = Uri.EscapeDataString(taskListId);
            var task = Uri.EscapeDataString(taskId);

            var select = GraphSelectBuilder.BuildSelect<GraphTodoLinkedResource>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/todo/lists/{list}/tasks/{task}/linkedResources" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphTodoLinkedResource>(url, ct);
        }

        internal Task<Stream> GetTaskStreamAsync(
            string userIdOrUpn,
            string taskListId,
            string taskId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var list = Uri.EscapeDataString(taskListId);
            var task = Uri.EscapeDataString(taskId);

            var select = GraphSelectBuilder.BuildSelect<GraphTodoTask>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/todo/lists/{list}/tasks/{task}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal Task<Stream> GetTaskChecklistItemStreamAsync(
            string userIdOrUpn,
            string taskListId,
            string taskId,
            string checklistItemId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var list = Uri.EscapeDataString(taskListId);
            var task = Uri.EscapeDataString(taskId);
            var item = Uri.EscapeDataString(checklistItemId);

            var select = GraphSelectBuilder.BuildSelect<GraphTodoChecklistItem>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/todo/lists/{list}/tasks/{task}/checklistItems/{item}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal Task<Stream> GetTaskLinkedResourceStreamAsync(
            string userIdOrUpn,
            string taskListId,
            string taskId,
            string linkedResourceId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var list = Uri.EscapeDataString(taskListId);
            var task = Uri.EscapeDataString(taskId);
            var lr = Uri.EscapeDataString(linkedResourceId);

            var select = GraphSelectBuilder.BuildSelect<GraphTodoLinkedResource>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/todo/lists/{list}/tasks/{task}/linkedResources/{lr}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }
    }

    internal OnenoteApiImpl OnenoteApi => new OnenoteApiImpl(_apiHelper);

    internal class OnenoteApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphNotebook> ListUserNotebooksAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            var select = GraphSelectBuilder.BuildSelect<GraphNotebook>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/onenote/notebooks" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphNotebook>(url, ct);
        }

        internal IAsyncEnumerable<GraphOnenoteSectionGroup> ListNotebookSectionGroupsAsync(string notebookId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var nb = Uri.EscapeDataString(notebookId);

            var select = GraphSelectBuilder.BuildSelect<GraphOnenoteSectionGroup>();
            var url =
                $"{baseUrl}/v1.0/onenote/notebooks/{nb}/sectionGroups" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphOnenoteSectionGroup>(url, ct);
        }

        internal IAsyncEnumerable<GraphOnenoteSectionGroup> ListSectionGroupSectionGroupsAsync(string sectionGroupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var sg = Uri.EscapeDataString(sectionGroupId);

            var select = GraphSelectBuilder.BuildSelect<GraphOnenoteSectionGroup>();
            var url =
                $"{baseUrl}/v1.0/onenote/sectionGroups/{sg}/sectionGroups" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphOnenoteSectionGroup>(url, ct);
        }
        internal IAsyncEnumerable<GraphOnenoteSection> ListSectionGroupSectionsAsync(string sectionGroupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var sg = Uri.EscapeDataString(sectionGroupId);

            var select = GraphSelectBuilder.BuildSelect<GraphOnenoteSection>();
            var url =
                $"{baseUrl}/v1.0/onenote/sectionGroups/{sg}/sections" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphOnenoteSection>(url, ct);
        }

        internal IAsyncEnumerable<GraphOnenotePage> ListSectionPagesAsync(string sectionId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var section = Uri.EscapeDataString(sectionId);

            var select = GraphSelectBuilder.BuildSelect<GraphOnenotePage>();
            var url =
                $"{baseUrl}/v1.0/onenote/sections/{section}/pages" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphOnenotePage>(url, ct);
        }

        internal Task<Stream> GetOnenotePageContentStreamAsync(string contentUrl, CancellationToken ct)
        {
            // contentUrl is typically an absolute Microsoft Graph URL. Use it as-is.
            // Page content is HTML; Accept header is optional but helps.
            return provider.GetGraphResponseAsRealStreamAsync(contentUrl, "text/html", ct);
        }
    }

    internal OneDriveApiImpl OneDriveApi => new OneDriveApiImpl(_apiHelper);

    internal class OneDriveApiImpl(APIHelper provider)
    {

        internal async Task<GraphDeltaResult<GraphDriveItem>> ListDriveDeltaAsync(
            string driveId,
            string? deltaLink,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);

            var select = GraphSelectBuilder.BuildSelect<GraphDriveItem>();
            var nextUrl = string.IsNullOrWhiteSpace(deltaLink)
                ? $"{baseUrl}/v1.0/drives/{drive}/root/delta" +
                  $"?$select={Uri.EscapeDataString(select)}" +
                  $"&$top={APIHelper.GENERAL_PAGE_SIZE}"
            : deltaLink;

            var items = new List<GraphDriveItem>();
            string? finalDeltaLink = null;

            while (!string.IsNullOrWhiteSpace(nextUrl))
            {
                ct.ThrowIfCancellationRequested();

                async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, new Uri(nextUrl));
                    req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct);
                    req.Headers.Accept.Clear();
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application / json"));
                    return req;
                }

                using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
                await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

                var page = await APIHelper.ParseResponseJson<GraphDeltaPage<GraphDriveItem>>(resp, ct).ConfigureAwait(false)
                           ?? new GraphDeltaPage<GraphDriveItem>();

                if (page.Value.Count > 0)
                    items.AddRange(page.Value);

                if (!string.IsNullOrWhiteSpace(page.DeltaLink))
                    finalDeltaLink = page.DeltaLink;

                nextUrl = page.NextLink;
            }

            if (string.IsNullOrWhiteSpace(finalDeltaLink))
                throw new UserInformationException("Drive delta did not return an @odata.deltaLink.", nameof(SourceProvider));

            return new GraphDeltaResult<GraphDriveItem>(items, finalDeltaLink);
        }

        internal IAsyncEnumerable<GraphDriveItem> ListDriveRootChildrenAsync(string driveId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);

            var select = GraphSelectBuilder.BuildSelect<GraphDriveItem>();
            var url =
                $"{baseUrl}/v1.0/drives/{drive}/root/children" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphDriveItem>(url, ct);
        }

        internal IAsyncEnumerable<GraphDrive> ListUserDrivesAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            var select = GraphSelectBuilder.BuildSelect<GraphDrive>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/drives" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            bool? EvaluateStatusCode(HttpResponseMessage m)
            {
                return m.StatusCode switch
                {
                    HttpStatusCode.ServiceUnavailable => false,
                    _ => null,
                };
            }

            // Map 503 to success, as it means the user has no OneDrive provisioned.
            return WithTryCatch(
                provider.GetAllGraphItemsAsync<GraphDrive>(url, EvaluateStatusCode, ct),
                ex => ex is HttpRequestException httpEx && httpEx.StatusCode == HttpStatusCode.ServiceUnavailable,
                ct);
        }

        private async IAsyncEnumerable<T> WithTryCatch<T>(IAsyncEnumerable<T> source, Func<Exception, bool>? ignoreException, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var enumerator = source.GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        yield break;
                }
                catch (Exception ex)
                {
                    if (ignoreException?.Invoke(ex) != true)
                        throw;

                    yield break;
                }

                yield return enumerator.Current;
            }
        }

        internal async Task<GraphDrive> GetUserPrimaryDriveAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            var select = GraphSelectBuilder.BuildSelect<GraphDrive>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/drive" +
                $"?$select={Uri.EscapeDataString(select)}";

            var drive = await provider.GetGraphItemAsync<GraphDrive>(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(drive.Id))
                throw new UserInformationException("Failed to read user's primary drive.", nameof(SourceProvider));

            return drive;
        }

        internal IAsyncEnumerable<GraphDriveItem> ListDriveFolderChildrenAsync(
            string driveId,
            string folderItemId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var folder = Uri.EscapeDataString(folderItemId);

            var select = GraphSelectBuilder.BuildSelect<GraphDriveItem>();
            var url =
                $"{baseUrl}/v1.0/drives/{drive}/items/{folder}/children" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphDriveItem>(url, ct);
        }

        internal Task<Stream> GetDriveItemContentStreamAsync(string driveId, string itemId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var item = Uri.EscapeDataString(itemId);

            // Returns the file content stream (302 redirect handled by HttpClient by default)
            var url = $"{baseUrl}/v1.0/drives/{drive}/items/{item}/content";
            return provider.GetGraphResponseAsRealStreamAsync(url, "application/octet-stream", ct);
        }

        internal IAsyncEnumerable<GraphPermission> GetDriveItemPermissionsAsync(
            string driveId,
            string itemId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var item = Uri.EscapeDataString(itemId);

            var select = GraphSelectBuilder.BuildSelect<GraphPermission>();
            var url =
                $"{baseUrl}/v1.0/drives/{drive}/items/{item}/permissions" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetAllGraphItemsAsync<GraphPermission>(url, ct);
        }

        internal Task<Stream> GetDriveItemMetadataStreamAsync(
            string driveId,
            string itemId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var item = Uri.EscapeDataString(itemId);

            var select =
                "id,name,parentReference,webUrl,eTag,cTag,size," +
                "createdDateTime,lastModifiedDateTime,createdBy,lastModifiedBy," +
                "fileSystemInfo,file,folder,package,shared,deleted";

            var url =
                $"{baseUrl}/v1.0/drives/{drive}/items/{item}?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }
    }

    internal CalendarApiImpl CalendarApi => new CalendarApiImpl(_apiHelper);

    internal class CalendarApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphCalendarGroup> ListUserCalendarGroupsAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            var select = GraphSelectBuilder.BuildSelect<GraphCalendarGroup>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/calendarGroups" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphCalendarGroup>(url, ct);
        }

        internal IAsyncEnumerable<GraphCalendar> ListUserCalendarsInCalendarGroupAsync(
            string userIdOrUpn,
            string calendarGroupId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var group = Uri.EscapeDataString(calendarGroupId);

            var select = GraphSelectBuilder.BuildSelect<GraphCalendar>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/calendarGroups/{group}/calendars" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphCalendar>(url, ct);
        }

        internal IAsyncEnumerable<GraphEvent> ListCalendarEventsAsync(string userIdOrUpn, string calendarId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var cal = Uri.EscapeDataString(calendarId);

            var select = GraphSelectBuilder.BuildSelect<GraphEvent>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/calendars/{cal}/events" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphEvent>(url, ct);
        }

        internal Task<Stream> GetCalendarEventStreamAsync(
            string userIdOrUpn,
            string calendarId,
            string eventId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var cal = Uri.EscapeDataString(calendarId);
            var ev = Uri.EscapeDataString(eventId);

            var select =
                "id,subject,createdDateTime,lastModifiedDateTime,start,end,isAllDay,isCancelled," +
                "location,organizer,attendees,recurrence,seriesMasterId,type,iCalUId," +
                "body,bodyPreview,importance,sensitivity,categories,showAs,responseStatus,onlineMeeting,webLink";

            var url =
                $"{baseUrl}/v1.0/users/{user}/calendars/{cal}/events/{ev}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal IAsyncEnumerable<GraphAttachment> ListCalendarEventAttachmentsAsync(
            string userIdOrUpn,
            string calendarId,
            string eventId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var cal = Uri.EscapeDataString(calendarId);
            var ev = Uri.EscapeDataString(eventId);

            var select = GraphSelectBuilder.BuildSelect<GraphAttachment>([
                nameof(GraphAttachment.ContentBytes),
                nameof(GraphAttachment.ContentId)
            ]);

            var url =
                $"{baseUrl}/v1.0/users/{user}/calendars/{cal}/events/{ev}/attachments" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphAttachment>(url, ct);
        }

        internal Task<Stream> GetCalendarEventAttachmentStreamAsync(
            string userIdOrUpn,
            string calendarId,
            string eventId,
            string attachmentId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var cal = Uri.EscapeDataString(calendarId);
            var ev = Uri.EscapeDataString(eventId);
            var att = Uri.EscapeDataString(attachmentId);

            var url = $"{baseUrl}/v1.0/users/{user}/calendars/{cal}/events/{ev}/attachments/{att}/$value";
            return provider.GetGraphResponseAsRealStreamAsync(url, "application/octet-stream", ct);
        }
    }

    internal UserPlannerApiImpl UserPlannerApi => new UserPlannerApiImpl(_apiHelper);

    internal class UserPlannerApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphPlannerTask> ListUserAssignedPlannerTasksAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            var select = GraphSelectBuilder.BuildSelect<GraphPlannerTask>();

            var url =
                $"{baseUrl}/v1.0/users/{user}/planner/tasks" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphPlannerTask>(url, ct);
        }
    }

    internal ChatApiImpl ChatApi => new ChatApiImpl(_apiHelper);
    internal class ChatApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphChat> ListUserChatsAsync(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            var select = GraphSelectBuilder.BuildSelect<GraphChat>();
            var url =
                $"{baseUrl}/v1.0/users/{user}/chats" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.CHATS_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphChat>(url, ct);
        }

        internal IAsyncEnumerable<GraphChatMember> ListChatMembersAsync(string chatId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var chat = Uri.EscapeDataString(chatId);

            var select = GraphSelectBuilder.BuildSelect<GraphChatMember>();
            var url =
                $"{baseUrl}/v1.0/chats/{chat}/members" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphChatMember>(url, ct);
        }

        internal IAsyncEnumerable<GraphChatMessage> ListChatMessagesAsync(string chatId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var chat = Uri.EscapeDataString(chatId);

            var select = GraphSelectBuilder.BuildSelect<GraphChatMessage>();

            var url =
                $"{baseUrl}/v1.0/chats/{chat}/messages" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphChatMessage>(url, ct);
        }


        public IAsyncEnumerable<GraphChatHostedContent> ListChatHostedContentsAsync(
            string chatId,
            string messageId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var chat = Uri.EscapeDataString(chatId);
            var msg = Uri.EscapeDataString(messageId);

            var select = GraphSelectBuilder.BuildSelect<GraphChatHostedContent>();
            var url =
                $"{baseUrl}/v1.0/chats/{chat}/messages/{msg}/hostedContents" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphChatHostedContent>(url, ct);
        }

        public Task<Stream> GetChatHostedContentValueStreamAsync(
            string chatId,
            string messageId,
            string hostedContentId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var chat = Uri.EscapeDataString(chatId);
            var msg = Uri.EscapeDataString(messageId);
            var hc = Uri.EscapeDataString(hostedContentId);

            var url =
                $"{baseUrl}/v1.0/chats/{chat}/messages/{msg}/hostedContents/{hc}/$value";

            return provider.GetGraphItemAsStreamAsync(url, "application/octet-stream", ct);
        }
    }
}
