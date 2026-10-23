// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.SourceProvider.ChangeTracking;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Gmail.v1;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// A message, read in its raw form
/// </summary>
/// <param name="provider">The provider</param>
/// <param name="stateKey">The change tracking key of the mailbox, so a failed download holds the
/// stored history id back, or <c>null</c> if changes are not tracked</param>
/// <param name="userId">The user whose mailbox holds the message</param>
/// <param name="parentPath">The path of the folder the message is listed in</param>
/// <param name="message">The message metadata</param>
/// <param name="service">The Gmail service of the user</param>
/// <param name="groupEmail">The group the message was found for, or <c>null</c> for a mailbox listing</param>
internal class GmailMessageSourceEntry(SourceProvider provider, string? stateKey, string userId, string parentPath, Message message, GmailService service, string? groupEmail)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, message.Id + GmailMailboxWalk.MESSAGE_EXTENSION),
           message.InternalDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).UtcDateTime : DateTime.UnixEpoch,
           message.InternalDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => message.SizeEstimate ?? 0;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.ChangeTracker.OpenWithFailureTrackingAsync(stateKey, message.Id + GmailMailboxWalk.MESSAGE_EXTENSION, async ct =>
        {
            var request = service.Users.Messages.Get(userId, message.Id);
            request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;
            var msg = await request.ExecuteAsync(ct).ConfigureAwait(false);

            // Raw is base64url encoded string
            var bytes = FromBase64Url(msg.Raw);
            return (Stream)new MemoryStream(bytes);
        }, cancellationToken);

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
        var messageId = message.Payload?.Headers?.FirstOrDefault(h => h.Name?.Equals("Message-Id", StringComparison.OrdinalIgnoreCase) == true)?.Value;
        var from = message.Payload?.Headers?.FirstOrDefault(h => h.Name?.Equals("From", StringComparison.OrdinalIgnoreCase) == true)?.Value;
        var date = message.Payload?.Headers?.FirstOrDefault(h => h.Name?.Equals("Date", StringComparison.OrdinalIgnoreCase) == true)?.Value;

        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GmailMessage.ToString() },
            { "gsuite:Name", string.IsNullOrWhiteSpace(subject) ? message.Id : subject },
            { "gsuite:Id", message.Id },
            { "gsuite:ThreadId", message.ThreadId },
            { "gsuite:Snippet", message.Snippet },
            { "gsuite:From", from },
            { "gsuite:Date", date },
            { "gsuite:Message-Id", messageId },
            { "gsuite:GroupEmail", groupEmail }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
