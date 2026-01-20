// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class CalendarEventContentSourceEntry(SourceProvider provider, string path, GraphUser user, GraphCalendar calendar, GraphEvent eventItem)
    : StreamResourceEntryFunction(
        SystemIO.IO_OS.PathCombine(path, "content.json"),
        eventItem.CreatedDateTime.FromGraphDateTime(),
        eventItem.LastModifiedDateTime.FromGraphDateTime(),
        -1,
        ct => provider.CalendarApi.GetCalendarEventStreamAsync(user.Id, calendar.Id, eventItem.Id, ct),
        minorMetadataFactory: ct => Task.FromResult(new Dictionary<string, string?>
        {
            { "o365:v", "1" },
            { "o365:Id", eventItem.Id },
            { "o365:Name", "content.json" },
            { "o365:Type", SourceItemType.CalendarEventContent.ToString() },
        }.Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value)))
{
}
