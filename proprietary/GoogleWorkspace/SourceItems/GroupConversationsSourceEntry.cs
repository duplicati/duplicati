// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Requests;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// Represents a folder containing conversations (emails) from a Google Group.
/// Uses the Gmail API to search for emails sent to the group address across all member mailboxes.
/// </summary>
internal class GroupConversationsSourceEntry(SourceProvider provider, string parentPath, string groupEmail)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "conversations")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Get group members to find one mailbox to search
        var memberEmails = await GetGroupMemberEmailsAsync(cancellationToken);

        // Pick the first member, or fall back to the admin's mailbox
        var userToSearch = memberEmails.Append("me").First();

        await foreach (var entry in EnumerateMessagesForUser(userToSearch, cancellationToken))
            yield return entry;
    }

    /// <summary>
    /// Gets the email addresses of all group members.
    /// </summary>
    private async Task<List<string>> GetGroupMemberEmailsAsync(CancellationToken cancellationToken)
    {
        var memberEmails = new List<string>();

        try
        {
            var service = provider.ApiHelper.GetDirectoryServiceForGroups();
            var request = service.Members.List(groupEmail);

            string? nextPageToken = null;
            do
            {
                if (cancellationToken.IsCancellationRequested) break;
                request.PageToken = nextPageToken;
                var response = await request.ExecuteAsync(cancellationToken);

                if (response.MembersValue != null)
                {
                    foreach (var member in response.MembersValue)
                    {
                        // Only include USER type members (not groups or other entity types)
                        // and members with an email address
                        if (member.Type == "USER" && !string.IsNullOrEmpty(member.Email))
                        {
                            memberEmails.Add(member.Email);
                        }
                    }
                }
                nextPageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(nextPageToken));
        }
        catch (Exception)
        {
            // If we can't get members, we'll fall back to searching the admin's mailbox
        }

        return memberEmails;
    }

    /// <summary>
    /// Enumerates messages for a specific user that were sent to the group.
    /// </summary>
    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateMessagesForUser(
        string userId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        GmailService service;
        try
        {
            service = provider.ApiHelper.GetGmailService(userId);
        }
        catch
        {
            // If we can't access this user's mailbox, skip them
            yield break;
        }

        // Search for emails sent to the group address
        var request = service.Users.Messages.List(userId);
        request.Q = $"to:{groupEmail} OR cc:{groupEmail}";

        do
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            ListMessagesResponse response;
            try
            {
                response = await request.ExecuteAsync(cancellationToken);
            }
            catch
            {
                // If we can't search this user's mailbox, skip them
                yield break;
            }

            if (response.Messages != null)
            {
                var batch = new BatchRequest(service);
                var messages = new List<Message>();

                foreach (var msg in response.Messages)
                {
                    var msgRequest = service.Users.Messages.Get(userId, msg.Id);
                    msgRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                    batch.Queue<Message>(msgRequest, (message, error, index, httpResponse) =>
                    {
                        if (error == null)
                        {
                            lock (messages)
                            {
                                messages.Add(message);
                            }
                        }
                    });
                }

                if (batch.Count > 0)
                {
                    await batch.ExecuteAsync(cancellationToken);
                }

                foreach (var message in messages)
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    yield return new GroupConversationMessageSourceEntry(provider, userId, this.Path, message, groupEmail);
                }
            }

            request.PageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(request.PageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GroupConversations.ToString() },
            { "gsuite:Name", "conversations" },
            { "gsuite:Id", groupEmail }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}

/// <summary>
/// Represents a single email message from a Google Group conversation.
/// </summary>
internal class GroupConversationMessageSourceEntry(SourceProvider provider, string userId, string parentPath, Message message, string groupEmail)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, message.Id + ".eml"),
           message.InternalDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).UtcDateTime : DateTime.UnixEpoch,
           message.InternalDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => message.SizeEstimate ?? 0;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetGmailService(userId);
        var request = service.Users.Messages.Get(userId, message.Id);
        request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;
        var msg = await request.ExecuteAsync(cancellationToken);

        // Raw is base64url encoded string
        var bytes = FromBase64Url(msg.Raw);
        return new MemoryStream(bytes);
    }

    private static byte[] FromBase64Url(string base64Url)
    {
        string padded = base64Url.Length % 4 == 0
            ? base64Url
            : base64Url + new string('=', 4 - base64Url.Length % 4);
        string base64 = padded.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var subject = message.Payload?.Headers?.FirstOrDefault(h => h.Name?.Equals("Subject", StringComparison.OrdinalIgnoreCase) == true)?.Value;
        var from = message.Payload?.Headers?.FirstOrDefault(h => h.Name?.Equals("From", StringComparison.OrdinalIgnoreCase) == true)?.Value;
        var date = message.Payload?.Headers?.FirstOrDefault(h => h.Name?.Equals("Date", StringComparison.OrdinalIgnoreCase) == true)?.Value;

        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GroupConversations.ToString() },
            { "gsuite:Name", string.IsNullOrWhiteSpace(subject) ? message.Id : subject },
            { "gsuite:Id", message.Id },
            { "gsuite:ThreadId", message.ThreadId },
            { "gsuite:Snippet", message.Snippet },
            { "gsuite:GroupEmail", groupEmail },
            { "gsuite:From", from },
            { "gsuite:Date", date }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
