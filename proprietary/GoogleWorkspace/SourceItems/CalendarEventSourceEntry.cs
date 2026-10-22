// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Calendar.v3.Data;
using System.Runtime.CompilerServices;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// An event, listed as a folder of its iCalendar form, its content and its attachments
/// </summary>
/// <param name="provider">The provider</param>
/// <param name="stateKey">The change tracking key of the calendar, so a failed attachment download holds the stored token back, or <c>null</c> if changes are not tracked</param>
/// <param name="parentPath">The path of the parent</param>
/// <param name="evt">The event</param>
/// <param name="driveService">The Drive service of the user, for attachments</param>
internal class CalendarEventSourceEntry(SourceProvider provider, string? stateKey, string parentPath, Event evt, DriveService driveService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, evt.Id)), evt.CreatedDateTimeOffset.HasValue ? evt.CreatedDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, evt.UpdatedDateTimeOffset.HasValue ? evt.UpdatedDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new CalendarEventICSSourceEntry(this.Path, evt);
        yield return new CalendarEventContentSourceEntry(this.Path, evt);

        if (evt.Attachments != null)
        {
            foreach (var attachment in evt.Attachments)
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return new CalendarEventAttachmentSourceEntry(provider, stateKey, this.Path, attachment, driveService);
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
