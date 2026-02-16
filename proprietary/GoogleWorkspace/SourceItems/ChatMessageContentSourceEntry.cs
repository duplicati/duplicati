// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.HangoutsChat.v1.Data;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatMessageContentSourceEntry(string parentPath, Message message)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "message.json"),
        message.CreateTimeDateTimeOffset.HasValue ? message.CreateTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch,
        DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message))));

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ChatMessageContent.ToString() },
            { "gsuite:Name", "message.json" },
            { "gsuite:Id", message.Name }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
