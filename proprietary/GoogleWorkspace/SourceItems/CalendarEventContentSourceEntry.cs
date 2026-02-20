// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Calendar.v3.Data;
using System.Text;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class CalendarEventContentSourceEntry(string parentPath, Event evt)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "content.json"),
        evt.CreatedDateTimeOffset.HasValue ? evt.CreatedDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch,
        evt.UpdatedDateTimeOffset.HasValue ? evt.UpdatedDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
        => new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt)));

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.CalendarEventContent.ToString() },
            { "gsuite:Name", "event.ics" },
            { "gsuite:Id", evt.Id },
            { "gsuite:HtmlLink", evt.HtmlLink }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
