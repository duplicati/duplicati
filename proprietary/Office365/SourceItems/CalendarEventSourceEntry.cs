// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class CalendarEventSourceEntry(SourceProvider provider, string path, GraphUser user, GraphCalendar calendar, GraphEvent eventItem)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, eventItem.Id)), null, null)
{
    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
        {
            { "o365:v", "1" },
            { "o365:Id", eventItem.Id },
            { "o365:UserId", user.Id },
            { "o365:CalendarName", calendar.Name },
            { "o365:CalUId", eventItem.ICalUId },
            { "o365:CalendarId", calendar.Id },
            { "o365:Name", eventItem.Subject },
            { "o365:Type", SourceItemType.CalendarEvent.ToString() },
            { "o365:Subject", eventItem.Subject },
            { "o365:Start", eventItem.Start?.ToString() },
            { "o365:End", eventItem.End?.ToString() },
        }.Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Yield content file
        yield return new CalendarEventContentSourceEntry(provider, this.Path, user, calendar, eventItem);

        // Yield attachments
        await foreach (var attachment in provider.CalendarApi.ListCalendarEventAttachmentsAsync(user.Id, calendar.Id, eventItem.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new CalendarEventAttachmentSourceEntry(provider, this.Path, user, calendar, eventItem, attachment);
        }
    }
}
