// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class CalendarSourceEntry(SourceProvider provider, string path, GraphUser user, GraphCalendarGroup calendarGroup, GraphCalendar calendar)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, calendar.Id)), null, null)
{
    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", calendar.Id },
                { "o365:Name", calendar.Name ?? "" },
                { "o365:Type", SourceItemType.Calendar.ToString() },
                { "o365:ChangeKey", calendar.ChangeKey ?? "" },
                { "o365:Color", calendar.Color ?? "" },
                { "o365:HexColor", calendar.HexColor ?? "" },
                { "o365:IsDefaultCalendar", calendar.IsDefaultCalendar.ToString() ?? "" },
                { "o365:IsRemovable", calendar.IsRemovable.ToString() ?? "" },
                { "o365:CanEdit", calendar.CanEdit.ToString() ?? "" },
                { "o365:CanShare", calendar.CanShare.ToString() ?? "" },
                { "o365:CanViewPrivateItems", calendar.CanViewPrivateItems.ToString() ?? "" },
                { "o365:IsTallyingResponses", calendar.IsTallyingResponses.ToString() ?? "" },
                { "o365:DefaultOnlineMeetingProvider", calendar.DefaultOnlineMeetingProvider ?? "" },
                { "o365:AllowedOnlineMeetingProviders", JsonSerializer.Serialize(calendar.AllowedOnlineMeetingProviders) ?? "" },
                { "o365:Owner", JsonSerializer.Serialize(calendar.Owner) ?? "" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var eventItem in provider.CalendarApi.ListCalendarEventsAsync(user.Id, calendar.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new CalendarEventSourceEntry(provider, this.Path, user, calendar, eventItem);
        }
    }
}
