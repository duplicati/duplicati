// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Keep.v1.Data;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class KeepNoteFileSourceEntry(string parentPath, Note note)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "note.json"),
        note.CreateTimeDateTimeOffset.HasValue ? note.CreateTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch,
        note.UpdateTimeDateTimeOffset.HasValue ? note.UpdateTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(note, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.KeepNote.ToString() },
            { "gsuite:Name", "note.json" },
            { "gsuite:id", note.Name }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
