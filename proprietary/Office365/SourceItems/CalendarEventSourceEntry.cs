// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class CalendarEventSourceEntry(SourceProvider provider, string path, GraphCalendarGroup calendarGroup, GraphCalendar calendar, GraphEvent eventItem)
    : StreamResourceEntryFunction(
        SystemIO.IO_OS.PathCombine(path, eventItem.Id),
        eventItem.CreatedDateTime.FromGraphDateTime(),
        eventItem.LastModifiedDateTime.FromGraphDateTime(),
        -1,
        ct => provider.CalendarApi.GetCalendarEventStreamAsync(calendarGroup.Id, calendar.Id, eventItem.Id, ct),
        minorMetadataFactory: ct => Task.FromResult(new Dictionary<string, string?>
        {
            { "o365:v", "1" },
            { "o365:Id", eventItem.Id },
            { "o365:Name", eventItem.Subject ?? "" },
            { "o365:Type", SourceItemType.CalendarEvent.ToString() },
            { "o365:Subject", eventItem.Subject ?? "" },
            { "o365:Start", eventItem.Start?.ToString() ??"" },
            { "o365:End", eventItem.End?.ToString() ?? "" },
        }.Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value)))
{
}
