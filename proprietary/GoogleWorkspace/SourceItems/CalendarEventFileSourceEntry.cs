// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Calendar.v3.Data;
using System.Text;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class CalendarEventFileSourceEntry(string parentPath, Event evt)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "event.ics"),
        evt.CreatedDateTimeOffset.HasValue ? evt.CreatedDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch,
        evt.UpdatedDateTimeOffset.HasValue ? evt.UpdatedDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//Duplicati//Google Workspace Backup//EN");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"UID:{evt.Id}");
        sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}");

        if (evt.Start != null)
        {
            if (evt.Start.DateTimeDateTimeOffset.HasValue)
                sb.AppendLine($"DTSTART:{evt.Start.DateTimeDateTimeOffset.Value.UtcDateTime:yyyyMMddTHHmmssZ}");
            else if (evt.Start.Date != null)
                sb.AppendLine($"DTSTART;VALUE=DATE:{evt.Start.Date.Replace("-", "")}");
        }

        if (evt.End != null)
        {
            if (evt.End.DateTimeDateTimeOffset.HasValue)
                sb.AppendLine($"DTEND:{evt.End.DateTimeDateTimeOffset.Value.UtcDateTime:yyyyMMddTHHmmssZ}");
            else if (evt.End.Date != null)
                sb.AppendLine($"DTEND;VALUE=DATE:{evt.End.Date.Replace("-", "")}");
        }

        if (!string.IsNullOrEmpty(evt.Summary)) sb.AppendLine($"SUMMARY:{evt.Summary}");
        if (!string.IsNullOrEmpty(evt.Description)) sb.AppendLine($"DESCRIPTION:{evt.Description}");
        if (!string.IsNullOrEmpty(evt.Location)) sb.AppendLine($"LOCATION:{evt.Location}");

        sb.AppendLine("END:VEVENT");
        sb.AppendLine("END:VCALENDAR");

        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.CalendarEvent.ToString() },
            { "gsuite:Name", "event.ics" },
            { "gsuite:id", evt.Id },
            { "gsuite:HtmlLink", evt.HtmlLink }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
