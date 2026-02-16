// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Gmail.v1;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class GmailMessageSourceEntry(string userId, string parentPath, Message message, GmailService service)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, message.Id + ".eml"),
           message.InternalDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).UtcDateTime : DateTime.UnixEpoch,
           message.InternalDate.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => message.SizeEstimate ?? 0;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
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
        var messageId = message.Payload?.Headers?.FirstOrDefault(h => h.Name?.Equals("Message-Id", StringComparison.OrdinalIgnoreCase) == true)?.Value;

        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GmailMessage.ToString() },
            { "gsuite:Name", string.IsNullOrWhiteSpace(subject) ? message.Id : subject },
            { "gsuite:Id", message.Id },
            { "gsuite:ThreadId", message.ThreadId },
            { "gsuite:Snippet", message.Snippet },
            { "gsuite:Message-Id", messageId },
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
