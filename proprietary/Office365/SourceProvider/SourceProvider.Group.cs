// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.CommandLine;
using System.Net.Http.Headers;

namespace Duplicati.Proprietary.Office365;

partial class SourceProvider
{
    internal GroupApiImpl GroupApi => new GroupApiImpl(_apiHelper);

    internal class GroupApiImpl(APIHelper provider)
    {
        public Task<Stream> GetGroupMetadataStreamAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select =
                "id,displayName,description,mail,mailEnabled,securityEnabled,visibility," +
                "groupTypes,resourceProvisioningOptions,createdDateTime";

            var url =
                $"{baseUrl}/v1.0/groups/{group}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        public IAsyncEnumerable<GraphDirectoryObject> ListGroupMembersAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphDirectoryObject>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/members" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphDirectoryObject>(url, ct);
        }

        public IAsyncEnumerable<GraphDirectoryObject> ListGroupOwnersAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphDirectoryObject>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/owners" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphDirectoryObject>(url, ct);
        }
    }

    internal GroupConversationApiImpl GroupConversationApi => new GroupConversationApiImpl(_apiHelper);

    internal sealed class GroupConversationApiImpl(APIHelper provider)
    {
        /// <summary>
        /// Lists the group conversations (mailbox-like “threads of discussion”).
        /// GET /groups/{id}/conversations
        /// </summary>
        internal IAsyncEnumerable<GraphConversation> ListGroupConversationsAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphConversation>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/conversations" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphConversation>(url, ct);
        }

        /// <summary>
        /// Lists all threads in a group.
        /// GET /groups/{id}/threads
        /// </summary>
        internal IAsyncEnumerable<GraphConversationThread> ListGroupThreadsAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphConversationThread>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/threads" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphConversationThread>(url, ct);
        }

        /// <summary>
        /// Lists threads for a specific conversation.
        /// GET /groups/{id}/conversations/{conversationId}/threads
        /// </summary>
        internal IAsyncEnumerable<GraphConversationThread> ListConversationThreadsAsync(
            string groupId,
            string conversationId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var conv = Uri.EscapeDataString(conversationId);

            var select = GraphSelectBuilder.BuildSelect<GraphConversationThread>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/conversations/{conv}/threads" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphConversationThread>(url, ct);
        }

        /// <summary>
        /// Lists posts in a thread.
        /// GET /groups/{id}/threads/{threadId}/posts
        /// (Also works as /groups/{id}/conversations/{conversationId}/threads/{threadId}/posts)
        /// </summary>
        internal IAsyncEnumerable<GraphPost> ListThreadPostsAsync(string groupId, string threadId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var thread = Uri.EscapeDataString(threadId);

            var select = GraphSelectBuilder.BuildSelect<GraphPost>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/threads/{thread}/posts" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphPost>(url, ct);
        }

        /// <summary>
        /// Returns a post (message) as JSON stream.
        /// GET /groups/{id}/threads/{threadId}/posts/{postId}
        /// </summary>
        internal Task<Stream> GetThreadPostStreamAsync(string groupId, string threadId, string postId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var thread = Uri.EscapeDataString(threadId);
            var post = Uri.EscapeDataString(postId);

            var select = GraphSelectBuilder.BuildSelect<GraphPost>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/threads/{thread}/posts/{post}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }
    }

    internal GroupCalendarApiImpl GroupCalendarApi => new GroupCalendarApiImpl(_apiHelper);

    internal sealed class GroupCalendarApiImpl(APIHelper provider)
    {
        internal Task<Stream> GetGroupCalendarStreamAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphCalendar>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/calendar" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal Task<GraphCalendar> GetGroupCalendarAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphCalendar>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/calendar" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsync<GraphCalendar>(url, ct);
        }

        internal IAsyncEnumerable<GraphEvent> ListGroupEventsAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphEvent>();

            // Underlying objects (series masters + single instances)
            var url =
                $"{baseUrl}/v1.0/groups/{group}/events" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphEvent>(url, ct);
        }

        internal Task<Stream> GetGroupEventStreamAsync(string groupId, string eventId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var ev = Uri.EscapeDataString(eventId);

            var select =
                "id,subject,createdDateTime,lastModifiedDateTime,start,end,isAllDay,isCancelled," +
                "location,organizer,attendees,recurrence,seriesMasterId,type," +
                "body,bodyPreview,importance,sensitivity,categories,showAs,responseStatus,onlineMeeting,webLink";

            var url =
                $"{baseUrl}/v1.0/groups/{group}/events/{ev}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }
    }

    internal DriveApiImpl GroupDriveApi => new DriveApiImpl(_apiHelper);

    internal sealed class DriveApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphDrive> ListGroupDrivesAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphDrive>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/drives" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphDrive>(url, ct);
        }
    }

    internal GroupPlannerApiImpl GroupPlannerApi => new GroupPlannerApiImpl(_apiHelper);

    internal sealed class GroupPlannerApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphPlannerPlan> ListGroupPlannerPlansAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphPlannerPlan>();
            var url =
                $"{baseUrl}/v1.0/groups/{group}/planner/plans" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphPlannerPlan>(url, ct);
        }
    }

    internal PlannerApiImpl PlannerApi => new PlannerApiImpl(_apiHelper);

    internal sealed class PlannerApiImpl(APIHelper provider)
    {
        internal IAsyncEnumerable<GraphPlannerBucket> ListPlannerBucketsAsync(string planId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var plan = Uri.EscapeDataString(planId);

            var select = GraphSelectBuilder.BuildSelect<GraphPlannerBucket>();
            var url =
                $"{baseUrl}/v1.0/planner/plans/{plan}/buckets" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphPlannerBucket>(url, ct);
        }

        internal IAsyncEnumerable<GraphPlannerTask> ListPlannerTasksAsync(string planId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var plan = Uri.EscapeDataString(planId);

            var select = GraphSelectBuilder.BuildSelect<GraphPlannerTask>();

            var url =
                $"{baseUrl}/v1.0/planner/plans/{plan}/tasks" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphPlannerTask>(url, ct);
        }

        internal Task<Stream> GetPlannerTaskDetailsStreamAsync(string taskId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var task = Uri.EscapeDataString(taskId);

            // Task details: description + checklist + references + previewType
            var select = "id,description,checklist,references,previewType";
            var url =
                $"{baseUrl}/v1.0/planner/tasks/{task}/details" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal Task<Stream> GetPlannerProgressTaskBoardFormatStreamAsync(string taskId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var task = Uri.EscapeDataString(taskId);

            var select = "id,orderHint";
            var url =
                $"{baseUrl}/v1.0/planner/tasks/{task}/progressTaskBoardFormat" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal Task<Stream> GetPlannerAssignedToTaskBoardFormatStreamAsync(string taskId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var task = Uri.EscapeDataString(taskId);

            var select = "id,orderHintsByAssignee";
            var url =
                $"{baseUrl}/v1.0/planner/tasks/{task}/assignedToTaskBoardFormat" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal Task<Stream> GetPlannerBucketTaskBoardFormatStreamAsync(string taskId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var task = Uri.EscapeDataString(taskId);

            var select = "id,orderHint";
            var url =
                $"{baseUrl}/v1.0/planner/tasks/{task}/bucketTaskBoardFormat" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }
    }

    internal GroupTeamsApiImpl GroupTeamsApi => new GroupTeamsApiImpl(_apiHelper);

    internal class GroupTeamsApiImpl(APIHelper provider)
    {
        public Task<Stream> GetGroupTeamStreamAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select =
                "id,displayName,description,createdDateTime,visibility,internalId,isArchived,webUrl";

            var url =
                $"{baseUrl}/v1.0/teams/{group}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal Task<Stream> GetTeamMetadataStreamAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);

            var select = "id,displayName,description,createdDateTime,visibility,isArchived,webUrl,internalId";
            var url =
                $"{baseUrl}/v1.0/teams/{team}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal IAsyncEnumerable<GraphTeamMember> ListTeamMembersAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphTeamMember>(
                // These will be returned but cannot be selected in the API call
                exclude: [nameof(GraphTeamMember.Email), nameof(GraphTeamMember.UserId)]
            );

            var url =
                $"{baseUrl}/v1.0/teams/{team}/members" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetAllGraphItemsAsync<GraphTeamMember>(url, ct);
        }

        internal IAsyncEnumerable<GraphChannel> ListTeamChannelsAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphChannel>();
            var url =
                $"{baseUrl}/v1.0/teams/{team}/channels" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetAllGraphItemsAsync<GraphChannel>(url, ct);
        }

        internal Task<Stream> GetTeamChannelStreamAsync(string groupId, string channelId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);

            var select = "id,displayName,description,membershipType,createdDateTime,isFavoriteByDefault,email,webUrl";
            var url =
                $"{baseUrl}/v1.0/teams/{team}/channels/{channel}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal IAsyncEnumerable<GraphChannelMessage> ListChannelMessagesAsync(string groupId, string channelId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);

            var url =
                $"{baseUrl}/v1.0/teams/{team}/channels/{channel}/messages" +
                $"?$top={APIHelper.CHATS_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphChannelMessage>(url, ct);
        }

        internal IAsyncEnumerable<GraphChannelMessage> ListChannelMessageRepliesAsync(
            string groupId,
            string channelId,
            string messageId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);
            var msg = Uri.EscapeDataString(messageId);

            var url =
                $"{baseUrl}/v1.0/teams/{team}/channels/{channel}/messages/{msg}/replies" +
                $"?$top={APIHelper.CHATS_PAGE_SIZE}";

            return provider.GetAllGraphItemsAsync<GraphChannelMessage>(url, ct);
        }

        internal Task<Stream> GetChannelMessageStreamAsync(
            string groupId,
            string channelId,
            string messageId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);
            var msg = Uri.EscapeDataString(messageId);

            var select =
                "id,replyToId,createdDateTime,lastModifiedDateTime,deletedDateTime,subject,from,body," +
                "attachments,mentions,reactions";

            var url =
                $"{baseUrl}/v1.0/teams/{team}/channels/{channel}/messages/{msg}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal Task<Stream> GetChannelMessageReplyStreamAsync(
            string groupId,
            string channelId,
            string messageId,
            string replyId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);
            var msg = Uri.EscapeDataString(messageId);
            var reply = Uri.EscapeDataString(replyId);

            var select =
                "id,replyToId,createdDateTime,lastModifiedDateTime,deletedDateTime,subject,from,body," +
                "attachments,mentions,reactions";

            var url =
                $"{baseUrl}/v1.0/teams/{team}/channels/{channel}/messages/{msg}/replies/{reply}" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal IAsyncEnumerable<GraphTeamsTab> ListChannelTabsAsync(string groupId, string channelId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);

            // configuration is a complex property (selectable) but NOT expandable
            var select = GraphSelectBuilder.BuildSelect<GraphTeamsTab>();

            var url =
                $"{baseUrl}/v1.0/teams/{team}/channels/{channel}/tabs" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$expand=teamsApp";

            return provider.GetAllGraphItemsAsync<GraphTeamsTab>(url, ct);
        }

        internal Task<Stream> GetChannelTabStreamAsync(string groupId, string channelId, string tabId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);
            var tab = Uri.EscapeDataString(tabId);

            // configuration is a complex property (selectable) but NOT expandable
            var select = GraphSelectBuilder.BuildSelect<GraphTeamsTab>();

            var url =
                $"{baseUrl}/v1.0/teams/{team}/channels/{channel}/tabs/{tab}" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$expand=teamsApp";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal IAsyncEnumerable<GraphTeamsAppInstallation> ListTeamInstalledAppsAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);

            var select = GraphSelectBuilder.BuildSelect<GraphTeamsAppInstallation>();
            var url =
                $"{baseUrl}/v1.0/teams/{team}/installedApps" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$expand=teamsApp,teamsAppDefinition";

            return provider.GetAllGraphItemsAsync<GraphTeamsAppInstallation>(url, ct);
        }

        internal Task<Stream> GetTeamInstalledAppStreamAsync(string groupId, string appId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);
            var app = Uri.EscapeDataString(appId);

            var select = GraphSelectBuilder.BuildSelect<GraphTeamsAppInstallation>();
            var url =
                $"{baseUrl}/v1.0/teams/{team}/installedApps/{app}" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$expand=teamsApp,teamsAppDefinition";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        internal async Task<bool> IsGroupTeamAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);

            async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri($"{baseUrl}/v1.0/teams/{team}"));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct);
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
            return true;
        }
    }
}
