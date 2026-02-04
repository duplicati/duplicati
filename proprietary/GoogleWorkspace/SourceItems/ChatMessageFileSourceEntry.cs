// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.HangoutsChat.v1.Data;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatMessageFileSourceEntry(string parentPath, Message message)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "message.json"),
        message.CreateTimeDateTimeOffset.HasValue ? message.CreateTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch,
        DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ChatMessage.ToString() },
            { "gsuite:Name", "message.json" },
            { "gsuite:id", message.Name }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
