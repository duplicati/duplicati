// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
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
            var team = Uri.EscapeDataString(groupId);

            // First verify that the Team exists (group may exist without Teams).
            var teamUrl = $"{baseUrl}/v1.0/teams/{team}";

            async Task<HttpRequestMessage> teamReqFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(teamUrl));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using (var teamResp = await provider.SendWithRetryShortAsync(teamReqFactory, ct).ConfigureAwait(false))
            {
                if (teamResp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null; // Not a Team (no threadId)

                await APIHelper.EnsureOfficeApiSuccessAsync(teamResp, ct).ConfigureAwait(false);
            }

            var select = "id,displayName,description,membershipType,createdDateTime,isFavoriteByDefault,email,webUrl";
            var url = $"{baseUrl}/v1.0/teams/{team}/channels?$select={Uri.EscapeDataString(select)}";

            await foreach (var channel in provider.GetAllGraphItemsAsync<GraphChannel>(url, ct))
            {
                if (string.Equals(channel.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    return channel;
            }

            return null;
        }

        internal async Task<bool> IsGroupTeamsEnabledAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);

            var url = $"{baseUrl}/v1.0/teams/{team}";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false; // Group exists but has no Team

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
            return true;
        }

        internal async Task<GraphChannel> CreateTeamChannelAsync(
            string groupId,
            string displayName,
            string? description,
            string? membershipType,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);

            var url = $"{baseUrl}/v1.0/teams/{team}/channels";

            var payload = new Dictionary<string, object?>
            {
                ["displayName"] = displayName,
                ["description"] = description,
                ["membershipType"] = membershipType ?? "standard"
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            return await provider.PostGraphItemAsync<GraphChannel>(url, content, ct).ConfigureAwait(false);
        }


        internal async Task<GraphChannel> CreateTeamChannelInMigrationModeAsync(
            string groupId,
            string displayName,
            string? description,
            string? membershipType,
            DateTimeOffset? createdDateTimeUtc,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);

            var url = $"{baseUrl}/v1.0/teams/{team}/channels";

            // Graph requires the instance annotation property name exactly.
            var payload = new Dictionary<string, object?>
            {
                ["displayName"] = displayName,
                ["description"] = description,
                ["membershipType"] = membershipType ?? "standard",
                ["@microsoft.graph.channelCreationMode"] = "migration"
            };

            // Optional for migration scenarios; must be UTC and not in the future.
            if (createdDateTimeUtc.HasValue)
                payload["createdDateTime"] = createdDateTimeUtc.Value.UtcDateTime.ToGraphTimeString();

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            return await provider.PostGraphItemAsync<GraphChannel>(url, content, ct).ConfigureAwait(false);
        }

        internal async Task<GraphChatMessage> ImportChannelMessageAsync(
            string groupId,
            string channelId,
            string senderUserId,
            string senderDisplayName,
            DateTimeOffset createdDateTimeUtc,
            string htmlContent,
            string? replyToId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);

            var url = $"{baseUrl}/v1.0/teams/{group}/channels/{channel}/messages";

            var payload = new Dictionary<string, object?>
            {
                ["createdDateTime"] = createdDateTimeUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                ["from"] = new
                {
                    user = new
                    {
                        id = senderUserId,
                        displayName = senderDisplayName,
                        userIdentityType = "aadUser"
                    }
                },
                ["body"] = new
                {
                    contentType = "html",
                    content = htmlContent
                }
            };

            if (!string.IsNullOrWhiteSpace(replyToId))
                payload["replyToId"] = replyToId;

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Headers.Add("Teamwork-Migrate", "true");
                req.Content = JsonContent.Create(payload);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphChatMessage>(stream, cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the imported message id.");

            return created;
        }

        internal IAsyncEnumerable<GraphChatMessage> ListChannelMessagesAsync(string groupId, string channelId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);

            var url = $"{baseUrl}/v1.0/teams/{group}/channels/{channel}/messages";
            return provider.GetAllGraphItemsAsync<GraphChatMessage>(url, ct);
        }

        internal IAsyncEnumerable<GraphChatMessage> ListChannelMessageRepliesAsync(string groupId, string channelId, string messageId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);
            var message = Uri.EscapeDataString(messageId);

            var url = $"{baseUrl}/v1.0/teams/{group}/channels/{channel}/messages/{message}/replies";
            return provider.GetAllGraphItemsAsync<GraphChatMessage>(url, ct);
        }

        internal async Task<GraphTeamsTab> CreateChannelTabAsync(string groupId, string channelId, string displayName, string? teamsAppId, GraphTeamsTabConfiguration? configuration, bool ignoreExisting, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var channel = Uri.EscapeDataString(channelId);

            var url = $"{baseUrl}/v1.0/teams/{group}/channels/{channel}/tabs";

            // Check if tab already exists
            if (!ignoreExisting)
            {
                var existingTabs = provider.GetAllGraphItemsAsync<GraphTeamsTab>(url, ct);
                await foreach (var tab in existingTabs)
                {
                    if (string.Equals(tab.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Tab already exists, return it
                        return tab;
                    }
                }
            }

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

    private async Task<string?> GetTargetChannelId(string groupId, string itemPath, CancellationToken cancel)
    {
        // Check map first
        var channelPath = _restoredChannelMap.Keys
            .Where(k => itemPath.StartsWith(Util.AppendDirSeparator(k)) || itemPath == k)
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();

        if (channelPath != null)
            return _restoredChannelMap[channelPath];

        // Walk up to find channel in metadata
        var currentPath = itemPath;
        while (!string.IsNullOrEmpty(currentPath))
        {
            if (_metadata.TryGetValue(currentPath, out var metadata) &&
                metadata.TryGetValue("o365:Type", out var type) &&
                type == SourceItemType.GroupChannel.ToString())
            {
                var displayName = metadata.GetValueOrDefault("o365:DisplayName") ?? metadata.GetValueOrDefault("o365:Name");
                if (string.IsNullOrWhiteSpace(displayName))
                    return null;

                try
                {
                    // Check if exists
                    var existingChannel = await GroupTeamsApi.GetTeamChannelAsync(groupId, displayName, cancel);
                    if (existingChannel != null)
                    {
                        await ChannelRestore.EnsureChannelInMigrationMode(existingChannel.Id, groupId, cancel);
                        _restoredChannelMap[currentPath] = existingChannel.Id;
                        return existingChannel.Id;
                    }

                    if (!await GroupTeamsApi.IsGroupTeamsEnabledAsync(groupId, cancel))
                    {
                        Log.WriteWarningMessage(LOGTAG, "GroupTeamsNotEnabled", null, $"Group {groupId} does not have Teams enabled, skipping channel create {displayName}");
                        return null;
                    }

                    // Create
                    var description = metadata.GetValueOrDefault("o365:Description");
                    var membershipType = metadata.GetValueOrDefault("o365:MembershipType");
                    var createdDateTimeStr = metadata.GetValueOrDefault("o365:CreatedDateTime");

                    var newChannel = await GroupTeamsApi.CreateTeamChannelAsync(groupId, displayName, description, membershipType, cancel);
                    await ChannelRestore.EnsureChannelInMigrationMode(newChannel.Id, groupId, cancel);
                    _restoredChannelMap[currentPath] = newChannel.Id;
                    return newChannel.Id;
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "GetTargetChannelIdFailed", ex, $"Failed to get/create channel {displayName}");
                    return null;
                }
            }
            currentPath = Path.GetDirectoryName(currentPath);
        }

        // Not found, use default channel
        return await ChannelRestore.GetTargetChannelId(groupId, cancel);
    }

    private async Task RestoreGroupChannels(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var channels = GetMetadataByType(SourceItemType.GroupChannel);
        if (channels.Count == 0)
            return;

        // If ID is missing we already logged warning
        var targetGroupId = await ChannelRestore.GetTargetGroupId(cancel);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return;

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
                if (!_ignoreExisting)
                {
                    var existingChannel = await GroupTeamsApi.GetTeamChannelAsync(targetGroupId, displayName, cancel);
                    if (existingChannel != null)
                    {
                        Log.WriteInformationMessage(LOGTAG, "RestoreGroupChannelsSkipExisting", null, $"Channel {displayName} already exists in target group, skipping.");
                        await ChannelRestore.EnsureChannelInMigrationMode(existingChannel.Id, targetGroupId, cancel);
                        _restoredChannelMap[originalPath] = existingChannel.Id;
                        continue;
                    }
                }

                if (!await GroupTeamsApi.IsGroupTeamsEnabledAsync(targetGroupId, cancel))
                {
                    Log.WriteWarningMessage(LOGTAG, "GroupTeamsNotEnabled", null, $"Group {targetGroupId} does not have Teams enabled, skipping channel create {displayName}");
                    continue;
                }

                var description = metadata.GetValueOrDefault("o365:Description");
                var membershipType = metadata.GetValueOrDefault("o365:MembershipType");
                var createdDateTimeStr = metadata.GetValueOrDefault("o365:CreatedDateTime");

                var newChannel = await GroupTeamsApi.CreateTeamChannelAsync(targetGroupId, displayName, description, membershipType, cancel);
                await ChannelRestore.EnsureChannelInMigrationMode(newChannel.Id, targetGroupId, cancel);
                _restoredChannelMap[originalPath] = newChannel.Id;
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupChannelsFailed", ex, $"Failed to restore channel {channel.Key}");
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

        // If ID is missing we already logged warning
        var targetGroupId = await ChannelRestore.GetTargetGroupId(cancel);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return;

        var existingMessagesCache = new Dictionary<string, List<GraphChatMessage>>();
        var existingRepliesCache = new Dictionary<string, List<GraphChatMessage>>();

        // Restore messages
        foreach (var message in messages)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = message.Key;

                // Find channel ID
                var channelId = await GetTargetChannelId(targetGroupId, originalPath, cancel);
                if (string.IsNullOrWhiteSpace(channelId))
                    return;

                if (!existingMessagesCache.ContainsKey(channelId))
                    existingMessagesCache[channelId] = await GroupTeamsApi.ListChannelMessagesAsync(targetGroupId, channelId, cancel).ToListAsync(cancel);

                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);
                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesMissingContent", null, $"Missing content for message {originalPath}, skipping.");
                    continue;
                }

                GraphChatMessage? graphMessage;
                using (var stream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    graphMessage = await JsonSerializer.DeserializeAsync<GraphChatMessage>(stream, cancellationToken: cancel);
                }

                if (graphMessage?.Body?.Content == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesMissingBody", null, $"Missing body content for message {originalPath}, skipping.");
                    continue;
                }

                var senderUserId = graphMessage.From?.User?.Id;
                var senderDisplayName = graphMessage.From?.User?.DisplayName ?? "Unknown User";
                var createdDateTime = graphMessage.CreatedDateTime ?? DateTimeOffset.UtcNow;

                if (string.IsNullOrWhiteSpace(senderUserId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesMissingSender", null, $"Missing sender for message {originalPath}, skipping.");
                    continue;
                }

                // Remove the "/content.json" suffix to get the message path so replies can find it
                var restoreMessagePath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath));

                if (!_ignoreExisting)
                {
                    var existing = existingMessagesCache[channelId].FirstOrDefault(m =>
                        m.CreatedDateTime.HasValue &&
                        Math.Abs((m.CreatedDateTime.Value - createdDateTime).TotalSeconds) < 2 &&
                        m.From?.User?.Id == senderUserId
                    );


                    if (existing != null)
                    {
                        Log.WriteInformationMessage(LOGTAG, "RestoreChannelMessagesSkipExisting", null, $"Message {originalPath} already exists, skipping.");
                        _restoredChannelMessageMap[restoreMessagePath] = existing.Id;

                        _metadata.TryRemove(originalPath, out _);
                        _temporaryFiles.TryRemove(originalPath, out var contentFileSkip);
                        contentFileSkip?.Dispose();
                        continue;
                    }
                }

                var newMessage = await GroupTeamsApi.ImportChannelMessageAsync(targetGroupId, channelId, senderUserId, senderDisplayName, createdDateTime, graphMessage.Body.Content, null, cancel);
                _restoredChannelMessageMap[restoreMessagePath] = newMessage.Id;
                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChannelMessagesFailed", ex, $"Failed to restore channel message {message.Key}");
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
                var channelId = await GetTargetChannelId(targetGroupId, originalPath, cancel);
                if (string.IsNullOrWhiteSpace(channelId))
                    return;

                // Find parent message ID
                var parentMessagePath = _restoredChannelMessageMap.Keys
                    .Where(k => originalPath.StartsWith(Util.AppendDirSeparator(k)))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                if (parentMessagePath == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesReplyMissingParent", null, $"Could not find restored parent message for reply {originalPath}, skipping.");
                    continue;
                }

                var parentMessageId = _restoredChannelMessageMap[parentMessagePath];

                if (!existingRepliesCache.ContainsKey(parentMessageId))
                    existingRepliesCache[parentMessageId] = await GroupTeamsApi.ListChannelMessageRepliesAsync(targetGroupId, channelId, parentMessageId, cancel).ToListAsync(cancel);

                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);
                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesReplyMissingContent", null, $"Missing content for reply {originalPath}, skipping.");
                    continue;
                }

                GraphChatMessage? graphMessage;
                using (var stream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    graphMessage = await JsonSerializer.DeserializeAsync<GraphChatMessage>(stream, cancellationToken: cancel);
                }

                if (graphMessage?.Body?.Content == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesReplyMissingBody", null, $"Missing body content for reply {originalPath}, skipping.");
                    continue;
                }

                var senderUserId = graphMessage.From?.User?.Id;
                var senderDisplayName = graphMessage.From?.User?.DisplayName ?? "Unknown User";
                var createdDateTime = graphMessage.CreatedDateTime ?? DateTimeOffset.UtcNow;

                if (string.IsNullOrWhiteSpace(senderUserId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelMessagesReplyMissingSender", null, $"Missing sender for reply {originalPath}, skipping.");
                    continue;
                }

                if (!_ignoreExisting)
                {
                    var existing = existingRepliesCache[parentMessageId].FirstOrDefault(m =>
                        m.CreatedDateTime.HasValue &&
                        Math.Abs((m.CreatedDateTime.Value - createdDateTime).TotalSeconds) < 2 &&
                        m.From?.User?.Id == senderUserId
                    );

                    if (existing != null)
                    {
                        Log.WriteInformationMessage(LOGTAG, "RestoreChannelMessagesReplySkipExisting", null, $"Reply {originalPath} already exists, skipping.");

                        _metadata.TryRemove(originalPath, out _);
                        _temporaryFiles.TryRemove(originalPath, out var contentFileSkip);
                        contentFileSkip?.Dispose();
                        continue;
                    }
                }

                await GroupTeamsApi.ImportChannelMessageAsync(targetGroupId, channelId, senderUserId, senderDisplayName, createdDateTime, graphMessage.Body.Content, parentMessageId, cancel);

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChannelMessagesReplyFailed", ex, $"Failed to restore channel message reply {reply.Key}");
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

        // If ID is missing we already logged warning
        var targetGroupId = await ChannelRestore.GetTargetGroupId(cancel);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return;

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
                    .Where(p => p.Key.StartsWith(Util.AppendDirSeparator(originalPath)))
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
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupConversationsThreadFailed", ex, $"Failed to restore thread {thread.Key}");
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
                    .Where(k => originalPath.StartsWith(Util.AppendDirSeparator(k)))
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
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupConversationsPostFailed", ex, $"Failed to restore post {post.Key}");
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

        // If ID is missing we already logged warning
        var targetGroupId = await ChannelRestore.GetTargetGroupId(cancel);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return;

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
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupCalendarEventsFailed", ex, $"Failed to restore group calendar event {eventItem.Key}");
            }
        }
    }

    /// <summary>
    /// Determines if the tab should be skipped during restore (built-in tabs).
    /// </summary>
    /// <param name="teamsAppId">The teams app ID to check.</param>
    /// <param name="entityId">The entity ID to check.</param>
    /// <returns><c>true</c> if the tab should be skipped; otherwise, <c>false</c>.</returns>
    internal static bool ShouldSkipTab(string? teamsAppId, string? entityId)
    {
        if (string.IsNullOrEmpty(teamsAppId))
            return true;

        if (!string.IsNullOrEmpty(entityId) && entityId.Equals("FileBrowserTabApp", StringComparison.OrdinalIgnoreCase))
            return true;

        return teamsAppId.Equals("com.microsoft.teamspace.tab.files", StringComparison.OrdinalIgnoreCase)
            || teamsAppId.Equals("com.microsoft.teamspace.tab.conversations", StringComparison.OrdinalIgnoreCase)
            || teamsAppId.Equals("com.microsoft.teamspace.tab.wiki", StringComparison.OrdinalIgnoreCase)
            || teamsAppId.Equals("com.microsoft.teamspace.tab.forms", StringComparison.OrdinalIgnoreCase)
            || teamsAppId.Equals("com.microsoft.teamspace.tab.stream", StringComparison.OrdinalIgnoreCase)
            || teamsAppId.Equals("com.microsoft.teamspace.tab.powerbi", StringComparison.OrdinalIgnoreCase)
            || teamsAppId.Equals("com.microsoft.teamspace.tab.whiteboard", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RestoreChannelTabs(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var tabs = GetMetadataByType(SourceItemType.GroupChannelTab);
        if (tabs.Count == 0)
            return;

        // If ID is missing we already logged warning
        var targetGroupId = await ChannelRestore.GetTargetGroupId(cancel);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return;

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
                var entityId = metadata.GetValueOrDefault("o365:EntityId");

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreChannelTabsMissingName", null, $"Missing display name for tab {originalPath}, skipping.");
                    continue;
                }

                if (ShouldSkipTab(teamsAppId, entityId))
                {
                    _metadata.TryRemove(originalPath, out _);
                    if (_temporaryFiles.TryRemove(originalPath, out var cf))
                        cf?.Dispose();

                    Log.WriteInformationMessage(LOGTAG, "RestoreChannelTabsSkipBuiltIn", $"Skipping built-in tab {displayName} for channel {originalPath}.");
                    continue;
                }

                // Find channel ID
                var channelId = await GetTargetChannelId(targetGroupId, originalPath, cancel);
                if (string.IsNullOrWhiteSpace(channelId))
                    return;

                // Construct configuration
                var configuration = new GraphTeamsTabConfiguration
                {
                    EntityId = metadata.GetValueOrDefault("o365:EntityId"),
                    ContentUrl = metadata.GetValueOrDefault("o365:ContentUrl"),
                    RemoveUrl = metadata.GetValueOrDefault("o365:RemoveUrl"),
                    WebsiteUrl = metadata.GetValueOrDefault("o365:WebsiteUrl")
                };

                await GroupTeamsApi.CreateChannelTabAsync(targetGroupId, channelId, displayName, teamsAppId, configuration, _ignoreExisting, cancel);
                _metadata.TryRemove(originalPath, out _);

                if (_temporaryFiles.TryRemove(originalPath, out var contentFile))
                {
                    contentFile.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreChannelTabsFailed", ex, $"Failed to restore channel tab {tab.Key}");
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

        // If ID is missing we already logged warning
        var targetGroupId = await ChannelRestore.GetTargetGroupId(cancel);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return;

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
                Log.WriteErrorMessage(LOGTAG, "RestoreTeamAppsFailed", ex, $"Failed to restore team app {app.Key}");
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

        // If ID is missing we already logged warning
        var targetGroupId = await ChannelRestore.GetTargetGroupId(cancel);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return;

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
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupSettingsFailed", ex, $"Failed to restore group settings {setting.Key}");
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

        // If ID is missing we already logged warning
        var targetGroupId = await ChannelRestore.GetTargetGroupId(cancel);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return;

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
                            Log.WriteWarningMessage(LOGTAG, "RestoreGroupMemberFailed", ex, $"Failed to add member {member.Id} to group");
                        }
                    }
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupMembersFailed", ex, $"Failed to restore group members {members.Key}");
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

        // If ID is missing we already logged warning
        var targetGroupId = await ChannelRestore.GetTargetGroupId(cancel);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return;

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
                            Log.WriteWarningMessage(LOGTAG, "RestoreGroupOwnerFailed", ex, $"Failed to add owner {owner.Id} to group");
                        }
                    }
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreGroupOwnersFailed", ex, $"Failed to restore group owners {owners.Key}");
            }
        }
    }

    private ChannelRestoreHelper? _channelRestoreHelper = null;

    internal ChannelRestoreHelper ChannelRestore => _channelRestoreHelper ??= new ChannelRestoreHelper(this);

    internal class ChannelRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetGroupId = null;
        private bool _hasLoadedGroupId = false;

        private ConcurrentDictionary<string, string> _targetChannelIds = new();
        private ConcurrentDictionary<string, string> _channelsInExplicitMigrationMode = new();

        public async Task<string?> GetTargetGroupId(CancellationToken cancel)
        {
            if (_hasLoadedGroupId)
                return _targetGroupId;

            _hasLoadedGroupId = true;

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            if (target.Type == SourceItemType.Group)
            {
                _targetGroupId = target.Metadata["o365:Id"]!;
            }
            else if (target.Type == SourceItemType.GroupTeams)
            {
                _targetGroupId = target.Metadata["o365:Id"]!;
            }
            else if (target.Type == SourceItemType.GroupChannel)
            {
                _targetGroupId = target.Metadata["o365:GroupId"]!;
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreChatInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring chat items.");
            }

            if (string.IsNullOrWhiteSpace(_targetGroupId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreChatMissingGroupId", null, $"Missing target groupId for restoring chat items.");
                return null;
            }

            return _targetGroupId;
        }

        public async Task<string?> GetTargetChannelId(string targetGroupId, CancellationToken cancel)
        {
            if (_targetChannelIds.TryGetValue(targetGroupId, out var channelId))
                return channelId;

            _targetChannelIds.TryAdd(targetGroupId, null!);

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            if (target.Type == SourceItemType.Group)
            {
                var groupId = await GetTargetGroupId(cancel);
                if (string.IsNullOrWhiteSpace(groupId))
                    return null;

                _targetChannelIds[targetGroupId] = await GetDefaultRestoreTargetChannel(groupId, cancel);
            }
            else if (target.Type == SourceItemType.GroupTeams)
            {
                var groupId = await GetTargetGroupId(cancel);
                if (string.IsNullOrWhiteSpace(groupId))
                    return null;

                _targetChannelIds[targetGroupId] = await GetDefaultRestoreTargetChannel(groupId, cancel);
            }
            else if (target.Type == SourceItemType.GroupChannel)
            {
                var groupId = await GetTargetGroupId(cancel);
                if (string.IsNullOrWhiteSpace(groupId))
                    return null;

                channelId = target.Metadata["o365:Id"];
                if (string.IsNullOrWhiteSpace(channelId))
                    return null;

                await EnsureChannelInMigrationMode(channelId, groupId, cancel);
                _targetChannelIds[targetGroupId] = channelId;
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreChatInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring chat items.");
                return null;
            }

            var res = _targetChannelIds.GetValueOrDefault(targetGroupId);

            if (string.IsNullOrWhiteSpace(res))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreChatMissingIds", null, $"Missing target userId or chatId for restoring chat items.");
                return null;
            }

            return res;
        }

        private async Task<string> GetDefaultRestoreTargetChannel(string groupId, CancellationToken cancel)
        {
            const string RESTORED_CHANNEL_NAME = "Restored";

            var existing = await Provider.ChannelApi.GetChannelByNameAsync(groupId, RESTORED_CHANNEL_NAME, cancel);
            if (existing != null)
            {
                await EnsureChannelInMigrationMode(existing.Id, groupId, cancel);
                return existing.Id;
            }

            var created = await Provider.GroupTeamsApi.CreateTeamChannelAsync(groupId, RESTORED_CHANNEL_NAME, "Restored content", null, cancel);
            await EnsureChannelInMigrationMode(created.Id, groupId, cancel);
            return created.Id;
        }

        public async Task EnsureChannelInMigrationMode(string channelId, string groupId, CancellationToken cancel)
        {
            if (_channelsInExplicitMigrationMode.ContainsKey(groupId))
                return;

            await Provider.ChannelApi.StartChannelMigrationAsync(groupId, channelId, cancel);
            _channelsInExplicitMigrationMode[groupId] = channelId;
        }


        public async Task EndMigrationMode(CancellationToken cancel)
        {
            foreach (var kvp in _channelsInExplicitMigrationMode)
            {
                try
                {
                    await Provider.ChannelApi.CompleteChannelMigrationAsync(kvp.Key, kvp.Value, cancel);
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "EndMigrationModeFailed", ex, $"Failed to end migration mode for channel {kvp.Value} in group {kvp.Key}");
                }
            }
            _channelsInExplicitMigrationMode.Clear();
        }
    }
}
