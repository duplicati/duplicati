// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class CalendarGroupSourceEntry(SourceProvider provider, string path, GraphUser user, GraphCalendarGroup group)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, group.Id)), null, null)
{
    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", group.Id },
                { "o365:Type", SourceItemType.CalendarGroup.ToString() },
                { "o365:Name", group.Name ?? "" },
                { "o365:ClassId", group.ClassId ?? "" },
                { "o365:ChangeKey", group.ChangeKey ?? "" }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var calendar in provider.CalendarApi.ListUserCalendarsInCalendarGroupAsync(user.Id, group.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new CalendarSourceEntry(provider, this.Path, user, calendar);
        }
    }
}
