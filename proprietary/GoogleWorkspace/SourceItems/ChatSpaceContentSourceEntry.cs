// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Google.Apis.HangoutsChat.v1.Data;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatSpaceContentSourceEntry(string parentPath, Space space)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "content.json"),
    space.CreateTimeDateTimeOffset.HasValue ? space.CreateTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch,
    DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(space))));

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ChatSpaceContent.ToString() },
            { "gsuite:Name", "message.json" },
            { "gsuite:Id", space.Name }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
