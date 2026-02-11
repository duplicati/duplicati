// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Calendar.v3.Data;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class CalendarEventSourceEntry(SourceProvider provider, string parentPath, Event evt)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, evt.Id)), evt.CreatedDateTimeOffset.HasValue ? evt.CreatedDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, evt.UpdatedDateTimeOffset.HasValue ? evt.UpdatedDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new CalendarEventFileSourceEntry(this.Path, evt);

        if (evt.Attachments != null)
        {
            foreach (var attachment in evt.Attachments)
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return new CalendarEventAttachmentSourceEntry(provider, this.Path, attachment);
            }
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.CalendarEvent.ToString() },
            { "gsuite:Name", evt.Summary ?? evt.Id },
            { "gsuite:Id", evt.Id },
            { "gsuite:HtmlLink", evt.HtmlLink }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
