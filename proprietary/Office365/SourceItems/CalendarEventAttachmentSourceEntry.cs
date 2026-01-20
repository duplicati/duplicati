// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class CalendarEventAttachmentSourceEntry(SourceProvider provider, string path, GraphUser user, GraphCalendar calendar, GraphEvent eventItem, GraphAttachment attachment)
    : StreamResourceEntryFunction(
        SystemIO.IO_OS.PathCombine(path, attachment.Id),
        DateTime.UnixEpoch,
        DateTime.UnixEpoch,
        attachment.Size ?? -1,
        ct => provider.CalendarApi.GetCalendarEventAttachmentStreamAsync(user.Id, calendar.Id, eventItem.Id, attachment.Id, ct),
        minorMetadataFactory: ct => Task.FromResult(new Dictionary<string, string?>
        {
            { "o365:v", "1" },
            { "o365:Id", attachment.Id },
            { "o365:Name", attachment.Name ?? "" },
            { "o365:Type", SourceItemType.CalendarEventAttachment.ToString() },
            { "o365:ContentType", attachment.ContentType ?? "" },
            { "o365:Size", attachment.Size?.ToString() ?? "" },
            { "o365:IsInline", attachment.IsInline?.ToString() ?? "" },
        }.Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value)))
{
}
