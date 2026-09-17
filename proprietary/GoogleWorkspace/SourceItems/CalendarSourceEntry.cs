// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Calendar.v3;
using System.Runtime.CompilerServices;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class CalendarSourceEntry(string parentPath, string userId, CalendarListEntry calendar, CalendarService service, DriveService driveService, CalendarService? aclService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, calendar.Id)), null, null)
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<CalendarSourceEntry>();

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;

        var request = service.Events.List(calendar.Id);

        // A calendar in the user's list may belong to an account whose Calendar service is
        // switched off, or that is archived, in which case its events cannot be read. Probe it
        // with the first page before emitting anything, so that the calendar is skipped as a
        // whole instead of producing a warning for its events and another for its ACL.
        Events events;
        try
        {
            events = await request.ExecuteAsync(cancellationToken);
        }
        catch (Exception ex) when (GoogleServiceErrors.IsServiceNotAvailableForUser(ex, SourceItemType.UserCalendar))
        {
            Log.WriteInformationMessage(LOGTAG, "CalendarNotAvailableForUser", Strings.CalendarNotAvailableForUser(calendar.Id, userId));
            yield break;
        }

        yield return new CalendarMetadataSourceEntry(this.Path, calendar);

        if (cancellationToken.IsCancellationRequested) yield break;
        if (aclService != null)
            yield return new CalendarAclSourceEntry(this.Path, calendar.Id, aclService);

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            if (!string.IsNullOrEmpty(nextPageToken))
            {
                request.PageToken = nextPageToken;
                events = await request.ExecuteAsync(cancellationToken);
            }

            if (events.Items != null)
            {
                foreach (var evt in events.Items)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return new CalendarEventSourceEntry(this.Path, evt, driveService);
                }
            }
            nextPageToken = events.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.Calendar.ToString() },
            { "gsuite:Name", calendar.SummaryOverride ?? calendar.Summary },
            { "gsuite:Id", calendar.Id },
            { "gsuite:Description", calendar.Description },
            { "gsuite:UserId", userId }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
