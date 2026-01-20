// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

public partial class RestoreProvider
{
    private readonly ConcurrentDictionary<string, string> _restoredConversationThreadMap = new();
    private readonly ConcurrentDictionary<string, string> _restoredChannelMessageMap = new();
    private readonly ConcurrentDictionary<string, string> _restoredChannelMap = new();

    internal GroupTeamsApiImpl GroupTeamsApi => new GroupTeamsApiImpl(_apiHelper);
    internal GroupConversationApiImpl GroupConversationApi => new GroupConversationApiImpl(_apiHelper);
    internal GroupCalendarApiImpl GroupCalendarApi => new GroupCalendarApiImpl(_apiHelper);
    internal GroupApiImpl GroupApi => new GroupApiImpl(_apiHelper);

    internal class GroupApiImpl(APIHelper provider)
    {
        internal async Task UpdateGroupAsync(string groupId, Stream contentStream, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var url = $"{baseUrl}/v1.0/groups/{group}";

            var graphGroup = await JsonSerializer.DeserializeAsync<GraphGroup>(contentStream, cancellationToken: ct);
            if (graphGroup == null) return;

            var payload = new Dictionary<string, object?>
            {
                ["description"] = graphGroup.Description,
                ["visibility"] = graphGroup.Visibility,
                ["mailEnabled"] = graphGroup.MailEnabled,
                ["securityEnabled"] = graphGroup.SecurityEnabled,
                // DisplayName is read-only for some group types or might require different permissions, but let's try
                ["displayName"] = graphGroup.DisplayName
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            await provider.PatchGraphItemAsync(url, content, ct);
        }

        internal async Task AddGroupMemberAsync(string groupId, string userId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var url = $"{baseUrl}/v1.0/groups/{group}/members/$ref";

            var payload = new
            {
                _odata_id = $"https://graph.microsoft.com/v1.0/directoryObjects/{userId}"
            };

            // Use a custom serializer option to handle the property name with @
            var json = JsonSerializer.Serialize(payload).Replace("_odata_id", "@odata.id");
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                await provider.PostGraphItemNoResponseAsync(url, content, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // Member might already exist or other issue
                // Graph API returns 400 if member already exists? Or 409?
                // Let's log and continue
                throw;
            }
        }

        internal async Task AddGroupOwnerAsync(string groupId, string userId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var url = $"{baseUrl}/v1.0/groups/{group}/owners/$ref";

            var payload = new
            {
                _odata_id = $"https://graph.microsoft.com/v1.0/directoryObjects/{userId}"
            };

            var json = JsonSerializer.Serialize(payload).Replace("_odata_id", "@odata.id");
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            await provider.PostGraphItemNoResponseAsync(url, content, ct);
        }
    }

    internal class GroupCalendarApiImpl(APIHelper provider)
    {
        internal async Task<GraphEvent> CreateGroupCalendarEventAsync(string groupId, GraphEvent eventItem, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var url = $"{baseUrl}/v1.0/groups/{group}/events";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
                => new HttpRequestMessage(HttpMethod.Post, new Uri(url))
                {
                    Headers =
                    {
                        Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false)
                    },
                    Content = JsonContent.Create(eventItem)
                };

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<GraphEvent>(respStream, cancellationToken: ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to deserialize created event");
        }
    }

    internal class GroupConversationApiImpl(APIHelper provider)
    {
        internal async Task<GraphConversationThread> CreateGroupThreadAsync(string groupId, string topic, object body, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var url = $"{baseUrl}/v1.0/groups/{group}/threads";

            var payload = new
            {
                topic,
                posts = new[]
                {
                    new { body }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            return await provider.PostGraphItemAsync<GraphConversationThread>(url, content, ct);
        }

        internal async Task ReplyToThreadAsync(string groupId, string threadId, object body, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var thread = Uri.EscapeDataString(threadId);

            var url = $"{baseUrl}/v1.0/groups/{group}/threads/{thread}/reply";

            var payload = new
            {
                post = new
                {
                    body
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            await provider.PostGraphItemNoResponseAsync(url, content, ct);
        }
    }

    internal class GroupTeamsApiImpl(APIHelper provider)
    {
        internal async Task<GraphChannel?> GetTeamChannelAsync(string groupId, string displayName, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var select = "id,displayName,description,membershipType,createdDateTime,isFavoriteByDefault,email,webUrl";

            // Note: Graph API filtering on channels might be limited. 
            // Let's just list all channels and find the one with matching name.

            var url = $"{baseUrl}/v1.0/teams/{group}/channels?$select={Uri.EscapeDataString(select)}";

            await foreach (var channel in provider.GetAllGraphItemsAsync<GraphChannel>(url, ct))
            {
                if (string.Equals(channel.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    return channel;
            }

            return null;
        }

        internal async Task<GraphChannel> CreateTeamChannelAsync(string groupId, string displayName, string? description, string? membershipType, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var url = $"{baseUrl}/v1.0/teams/{group}/channels";

            var payload = new
            {
                displayName,
                description,
                membershipType = membershipType ?? "standard"
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            return await provider.PostGraphItemAsync<GraphChannel>(url, content, ct);
        }

        internal async Task<GraphChatMessage> CreateChannelMessageAsync(string groupId, string channelId, object body, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);

            var url = $"{baseUrl}/v1.0/teams/{group}/channels/{channel}/messages";

            var payload = new
            {
                body
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            return await provider.PostGraphItemAsync<GraphChatMessage>(url, content, ct);
        }

        internal async Task ReplyToChannelMessageAsync(string groupId, string channelId, string messageId, object body, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);
            var message = Uri.EscapeDataString(messageId);

            var url = $"{baseUrl}/v1.0/teams/{group}/channels/{channel}/messages/{message}/replies";

            var payload = new
            {
                body
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            await provider.PostGraphItemNoResponseAsync(url, content, ct);
        }

        internal async Task<GraphTeamsTab> CreateChannelTabAsync(string groupId, string channelId, string displayName, string? teamsAppId, GraphTeamsTabConfiguration? configuration, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);

            var url = $"{baseUrl}/v1.0/teams/{group}/channels/{channel}/tabs";

            var payload = new Dictionary<string, object?>
            {
                ["displayName"] = displayName,
                ["configuration"] = configuration
            };

            if (!string.IsNullOrEmpty(teamsAppId))
            {
                payload["teamsApp@odata.bind"] = $"https://graph.microsoft.com/v1.0/appCatalogs/teamsApps/{teamsAppId}";
            }

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            return await provider.PostGraphItemAsync<GraphTeamsTab>(url, content, ct);
        }

        internal async Task InstallTeamAppAsync(string groupId, string teamsAppId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);

            var url = $"{baseUrl}/v1.0/teams/{group}/installedApps";

            var payloadBind = new Dictionary<string, string>
            {
                ["teamsApp@odata.bind"] = $"https://graph.microsoft.com/v1.0/appCatalogs/teamsApps/{teamsAppId}"
            };

            var json = JsonSerializer.Serialize(payloadBind);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            await provider.PostGraphItemNoResponseAsync(url, content, ct);
        }
    }

    private async Task RestoreGroupChannels(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var channels = GetMetadataByType(SourceItemType.GroupChannel);
        if (channels.Count == 0)
            return;

        // We need the target group ID.
        // If we are restoring to a Group, RestoreTarget.Metadata["o365:Id"] should be the group ID.

        string? targetGroupId = null;
        if (RestoreTarget.Type == SourceItemType.Group)
        {
            targetGroupId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreGroupChannelsMissingTarget", null, "Could not determine target group ID for restoring channels.");
            return;
        }

        foreach (var channel in channels)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = channel.Key;
                var metadata = channel.Value;
                var displayName = metadata.GetValueOrDefault("o365:DisplayName") ?? metadata.GetValueOrDefault("o365:Name");

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupChannelsMissingName", null, $"Missing display name for channel {originalPath}, skipping.");
                    continue;
                }

                // Check if channel exists
                var existingChannel = await GroupTeamsApi.GetTeamChannelAsync(targetGroupId, displayName, cancel);
                if (existingChannel != null)
                {
                    Log.WriteInformationMessage(LOGTAG, "RestoreGroupChannelsSkipExisting", null, $"Channel {displayName} already exists in target group, skipping.");
                    _restoredChannelMap[originalPath] = existingChannel.Id;
                    continue;
                }

                var description = metadata.GetValueOrDefault("o365:Description");
                var membershipType = metadata.GetValueOrDefault("o365:MembershipType");

                var newChannel = await GroupTeamsApi.CreateTeamChannelAsync(targetGroupId, displayName, description, membershipType, cancel);
                _restoredChannelMap[originalPath] = newChannel.Id;
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupChannelsFailed", ex, $"Failed to restore channel {channel.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreChannelMessages(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var messages = GetMetadataByType(SourceItemType.GroupChannelMessage);
        var replies = GetMetadataByType(SourceItemType.GroupChannelMessageReply);

        if (messages.Count == 0 && replies.Count == 0)
            return;

        string? targetGroupId = null;
        if (RestoreTarget.Type == SourceItemType.Group)
        {
            targetGroupId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesMissingTarget", null, "Could not determine target group ID for restoring channel messages.");
            return;
        }

        // Restore messages
        foreach (var message in messages)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = message.Key;

                // Find channel ID
                var channelPath = _restoredChannelMap.Keys
                    .Where(k => originalPath.StartsWith(k + Path.DirectorySeparatorChar))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                if (channelPath == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesMissingChannel", null, $"Could not find restored channel for message {originalPath}, skipping.");
                    continue;
                }

                var channelId = _restoredChannelMap[channelPath];

                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);
                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesMissingContent", null, $"Missing content for message {originalPath}, skipping.");
                    continue;
                }

                object? body = null;
                using (var stream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    var graphMessage = await JsonSerializer.DeserializeAsync<GraphChatMessage>(stream, cancellationToken: cancel);
                    body = graphMessage?.Body;
                }

                if (body == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesMissingBody", null, $"Missing body for message {originalPath}, skipping.");
                    continue;
                }

                var newMessage = await GroupTeamsApi.CreateChannelMessageAsync(targetGroupId, channelId, body, cancel);
                _restoredChannelMessageMap[originalPath] = newMessage.Id;

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChannelMessagesFailed", ex, $"Failed to restore channel message {message.Key}: {ex.Message}");
            }
        }

        // Restore replies
        foreach (var reply in replies)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = reply.Key;

                // Find channel ID
                var channelPath = _restoredChannelMap.Keys
                    .Where(k => originalPath.StartsWith(k + Path.DirectorySeparatorChar))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                if (channelPath == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesReplyMissingChannel", null, $"Could not find restored channel for reply {originalPath}, skipping.");
                    continue;
                }

                var channelId = _restoredChannelMap[channelPath];

                // Find parent message ID
                var parentMessagePath = _restoredChannelMessageMap.Keys
                    .Where(k => originalPath.StartsWith(k + Path.DirectorySeparatorChar))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                if (parentMessagePath == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesReplyMissingParent", null, $"Could not find restored parent message for reply {originalPath}, skipping.");
                    continue;
                }

                var parentMessageId = _restoredChannelMessageMap[parentMessagePath];

                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);
                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesReplyMissingContent", null, $"Missing content for reply {originalPath}, skipping.");
                    continue;
                }

                object? body = null;
                using (var stream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    var graphMessage = await JsonSerializer.DeserializeAsync<GraphChatMessage>(stream, cancellationToken: cancel);
                    body = graphMessage?.Body;
                }

                if (body == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesReplyMissingBody", null, $"Missing body for reply {originalPath}, skipping.");
                    continue;
                }

                await GroupTeamsApi.ReplyToChannelMessageAsync(targetGroupId, channelId, parentMessageId, body, cancel);

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChannelMessagesReplyFailed", ex, $"Failed to restore channel message reply {reply.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreGroupConversations(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var threads = GetMetadataByType(SourceItemType.GroupConversationThread);
        var posts = GetMetadataByType(SourceItemType.GroupConversationThreadPost);

        if (threads.Count == 0 && posts.Count == 0)
            return;

        string? targetGroupId = null;
        if (RestoreTarget.Type == SourceItemType.Group)
        {
            targetGroupId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreGroupConversationsMissingTarget", null, "Could not determine target group ID for restoring conversations.");
            return;
        }

        // Restore threads
        foreach (var thread in threads)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = thread.Key;
                var metadata = thread.Value;
                var topic = metadata.GetValueOrDefault("o365:Topic") ?? "Restored Thread";

                // Find the first post for this thread to create it
                var threadPosts = posts
                    .Where(p => p.Key.StartsWith(originalPath + Path.DirectorySeparatorChar))
                    .OrderBy(p => p.Value.GetValueOrDefault("o365:CreatedDateTime"))
                    .ToList();

                if (threadPosts.Count == 0)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupConversationsEmptyThread", null, $"Thread {originalPath} has no posts, skipping.");
                    continue;
                }

                var firstPost = threadPosts[0];
                var firstPostPath = firstPost.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(firstPostPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupConversationsMissingContent", null, $"Missing content for first post of thread {originalPath}, skipping.");
                    continue;
                }

                object? body = null;
                using (var stream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    var graphPost = await JsonSerializer.DeserializeAsync<GraphPost>(stream, cancellationToken: cancel);
                    body = graphPost?.Body;
                }

                if (body == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupConversationsMissingBody", null, $"Missing body for first post of thread {originalPath}, skipping.");
                    continue;
                }

                var newThread = await GroupConversationApi.CreateGroupThreadAsync(targetGroupId, topic, body, cancel);
                _restoredConversationThreadMap[originalPath] = newThread.Id;

                // Remove first post from metadata so we don't restore it again as a reply
                _metadata.TryRemove(firstPostPath, out _);
                _temporaryFiles.TryRemove(firstPostPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupConversationsThreadFailed", ex, $"Failed to restore thread {thread.Key}: {ex.Message}");
            }
        }

        // Restore remaining posts as replies
        // Re-fetch posts because we might have removed some
        posts = GetMetadataByType(SourceItemType.GroupConversationThreadPost);

        foreach (var post in posts)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = post.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupConversationsMissingPostContent", null, $"Missing content for post {originalPath}, skipping.");
                    continue;
                }

                // Find thread ID
                var threadPath = _restoredConversationThreadMap.Keys
                    .Where(k => originalPath.StartsWith(k + Path.DirectorySeparatorChar))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                if (threadPath == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupConversationsMissingThread", null, $"Could not find restored thread for post {originalPath}, skipping.");
                    continue;
                }

                var threadId = _restoredConversationThreadMap[threadPath];

                object? body = null;
                using (var stream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    var graphPost = await JsonSerializer.DeserializeAsync<GraphPost>(stream, cancellationToken: cancel);
                    body = graphPost?.Body;
                }

                if (body == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupConversationsMissingPostBody", null, $"Missing body for post {originalPath}, skipping.");
                    continue;
                }

                await GroupConversationApi.ReplyToThreadAsync(targetGroupId, threadId, body, cancel);

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupConversationsPostFailed", ex, $"Failed to restore post {post.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreGroupCalendarEvents(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var events = GetMetadataByType(SourceItemType.GroupCalendarEvent);
        if (events.Count == 0)
            return;

        string? targetGroupId = null;
        if (RestoreTarget.Type == SourceItemType.Group)
        {
            targetGroupId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreGroupCalendarEventsMissingTarget", null, "Could not determine target group ID for restoring calendar events.");
            return;
        }

        foreach (var eventItem in events)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = eventItem.Key;

                using var stream = await OpenRead(originalPath, cancel);
                var graphEvent = await JsonSerializer.DeserializeAsync<GraphEvent>(stream, cancellationToken: cancel);

                if (graphEvent == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupCalendarEventsInvalidJson", null, $"Invalid JSON for event {originalPath}, skipping.");
                    continue;
                }

                // Clean up properties that shouldn't be sent on creation
                graphEvent.Id = "";
                graphEvent.CreatedDateTime = null;
                graphEvent.LastModifiedDateTime = null;
                // Group events don't support all properties that user events do, but Graph API should handle it or ignore extra fields.
                // However, we should be careful about attendees if it's a group event.
                // Usually group events are just added to the group calendar.

                await GroupCalendarApi.CreateGroupCalendarEventAsync(targetGroupId, graphEvent, cancel);
                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupCalendarEventsFailed", ex, $"Failed to restore group calendar event {eventItem.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreChannelTabs(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var tabs = GetMetadataByType(SourceItemType.GroupChannelTab);
        if (tabs.Count == 0)
            return;

        string? targetGroupId = null;
        if (RestoreTarget.Type == SourceItemType.Group)
        {
            targetGroupId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreChannelTabsMissingTarget", null, "Could not determine target group ID for restoring channel tabs.");
            return;
        }

        foreach (var tab in tabs)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = tab.Key;
                var metadata = tab.Value;
                var displayName = metadata.GetValueOrDefault("o365:DisplayName");
                var teamsAppId = metadata.GetValueOrDefault("o365:TeamsAppId");

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelTabsMissingName", null, $"Missing display name for tab {originalPath}, skipping.");
                    continue;
                }

                // Find channel ID
                var channelPath = _restoredChannelMap.Keys
                    .Where(k => originalPath.StartsWith(k + Path.DirectorySeparatorChar))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                if (channelPath == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelTabsMissingChannel", null, $"Could not find restored channel for tab {originalPath}, skipping.");
                    continue;
                }

                var channelId = _restoredChannelMap[channelPath];

                // Construct configuration
                var configuration = new GraphTeamsTabConfiguration
                {
                    EntityId = metadata.GetValueOrDefault("o365:EntityId"),
                    ContentUrl = metadata.GetValueOrDefault("o365:ContentUrl"),
                    RemoveUrl = metadata.GetValueOrDefault("o365:RemoveUrl"),
                    WebsiteUrl = metadata.GetValueOrDefault("o365:WebsiteUrl")
                };

                await GroupTeamsApi.CreateChannelTabAsync(targetGroupId, channelId, displayName, teamsAppId, configuration, cancel);
                _metadata.TryRemove(originalPath, out _);

                if (_temporaryFiles.TryRemove(originalPath, out var contentFile))
                {
                    contentFile.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChannelTabsFailed", ex, $"Failed to restore channel tab {tab.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreTeamApps(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var apps = GetMetadataByType(SourceItemType.GroupInstalledApp);
        if (apps.Count == 0)
            return;

        string? targetGroupId = null;
        if (RestoreTarget.Type == SourceItemType.Group)
        {
            targetGroupId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreTeamAppsMissingTarget", null, "Could not determine target group ID for restoring team apps.");
            return;
        }

        foreach (var app in apps)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = app.Key;
                var metadata = app.Value;
                var teamsAppId = metadata.GetValueOrDefault("o365:TeamsAppId");

                if (string.IsNullOrWhiteSpace(teamsAppId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTeamAppsMissingAppId", null, $"Missing Teams App ID for app {originalPath}, skipping.");
                    continue;
                }

                await GroupTeamsApi.InstallTeamAppAsync(targetGroupId, teamsAppId, cancel);
                _metadata.TryRemove(originalPath, out _);

                if (_temporaryFiles.TryRemove(originalPath, out var contentFile))
                {
                    contentFile.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreTeamAppsFailed", ex, $"Failed to restore team app {app.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreGroupSettings(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var settings = GetMetadataByType(SourceItemType.GroupSettings);
        if (settings.Count == 0)
            return;

        string? targetGroupId = null;
        if (RestoreTarget.Type == SourceItemType.Group)
        {
            targetGroupId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreGroupSettingsMissingTarget", null, "Could not determine target group ID for restoring group settings.");
            return;
        }

        foreach (var setting in settings)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = setting.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupSettingsMissingContent", null, $"Missing content for {originalPath}, skipping.");
                    continue;
                }

                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    await GroupApi.UpdateGroupAsync(targetGroupId, contentStream, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupSettingsFailed", ex, $"Failed to restore group settings {setting.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreGroupMembers(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var membersList = GetMetadataByType(SourceItemType.GroupMember);
        if (membersList.Count == 0)
            return;

        string? targetGroupId = null;
        if (RestoreTarget.Type == SourceItemType.Group)
        {
            targetGroupId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreGroupMembersMissingTarget", null, "Could not determine target group ID for restoring group members.");
            return;
        }

        foreach (var members in membersList)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = members.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupMembersMissingContent", null, $"Missing content for {originalPath}, skipping.");
                    continue;
                }

                List<GraphDirectoryObject>? memberObjects;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    memberObjects = await JsonSerializer.DeserializeAsync<List<GraphDirectoryObject>>(contentStream, cancellationToken: cancel);
                }

                if (memberObjects != null)
                {
                    foreach (var member in memberObjects)
                    {
                        if (cancel.IsCancellationRequested) break;
                        try
                        {
                            await GroupApi.AddGroupMemberAsync(targetGroupId, member.Id, cancel);
                        }
                        catch (Exception ex)
                        {
                            Log.WriteWarningMessage(LOGTAG, "RestoreGroupMemberFailed", ex, $"Failed to add member {member.Id} to group: {ex.Message}");
                        }
                    }
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupMembersFailed", ex, $"Failed to restore group members {members.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreGroupOwners(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var ownersList = GetMetadataByType(SourceItemType.GroupOwner);
        if (ownersList.Count == 0)
            return;

        string? targetGroupId = null;
        if (RestoreTarget.Type == SourceItemType.Group)
        {
            targetGroupId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreGroupOwnersMissingTarget", null, "Could not determine target group ID for restoring group owners.");
            return;
        }

        foreach (var owners in ownersList)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = owners.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreGroupOwnersMissingContent", null, $"Missing content for {originalPath}, skipping.");
                    continue;
                }

                List<GraphDirectoryObject>? ownerObjects;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    ownerObjects = await JsonSerializer.DeserializeAsync<List<GraphDirectoryObject>>(contentStream, cancellationToken: cancel);
                }

                if (ownerObjects != null)
                {
                    foreach (var owner in ownerObjects)
                    {
                        if (cancel.IsCancellationRequested) break;
                        try
                        {
                            await GroupApi.AddGroupOwnerAsync(targetGroupId, owner.Id, cancel);
                        }
                        catch (Exception ex)
                        {
                            Log.WriteWarningMessage(LOGTAG, "RestoreGroupOwnerFailed", ex, $"Failed to add owner {owner.Id} to group: {ex.Message}");
                        }
                    }
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupOwnersFailed", ex, $"Failed to restore group owners {owners.Key}: {ex.Message}");
            }
        }
    }
}
