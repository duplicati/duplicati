// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Calendar.v3;
using System.Runtime.CompilerServices;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// A calendar, listing its metadata, its ACL and its events. When the provider tracks changes
/// and a sync token is stored for the calendar, only the events created, changed or deleted
/// since the previous backup are listed, and the rest is carried forward by the backup.
/// </summary>
/// <param name="provider">The provider</param>
/// <param name="parentPath">The path of the parent</param>
/// <param name="userId">The user whose calendar list the calendar is in</param>
/// <param name="calendar">The calendar</param>
/// <param name="service">The Calendar service of the user</param>
/// <param name="driveService">The Drive service of the user, for attachments</param>
/// <param name="aclService">The Calendar service with write scope, for the ACL, or <c>null</c> to skip the ACL</param>
internal class CalendarSourceEntry(SourceProvider provider, string parentPath, string userId, CalendarListEntry calendar, CalendarService service, DriveService driveService, CalendarService? aclService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, calendar.Id)), null, null)
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<CalendarSourceEntry>();

    /// <summary>
    /// The status of an event that was deleted, or of a cancelled instance of a recurring event
    /// </summary>
    private const string CANCELLED_STATUS = "cancelled";

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;

        var tracker = provider.ChangeTracker;
        var useDelta = provider.CalendarDeltaActive;
        var stateKey = useDelta ? ChangeTrackingKeys.Calendar(userId, calendar.Id) : null;
        var stored = stateKey == null ? null : tracker.GetState(stateKey);

        // Decide what the carry-forward sees before anything can fail: with a token, the
        // calendar is walked incrementally and unmarked paths below it are copied from the
        // previous backup; without one, the whole calendar is listed and the copy is wrong
        if (stored != null)
            tracker.MarkIncremental(Path);
        else if (useDelta)
            tracker.MarkExaminedSubtree(Path);

        var completed = false;
        try
        {
            var request = CreateRequest(stored);

            // A calendar in the user's list may belong to an account whose Calendar service is
            // switched off, or that is archived, in which case its events cannot be read. Probe it
            // with the first page before emitting anything, so that the calendar is skipped as a
            // whole instead of producing a warning for its events and another for its ACL.
            Events events;
            try
            {
                events = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (GoogleServiceErrors.IsServiceNotAvailableForUser(ex, SourceItemType.UserCalendar))
            {
                // Nothing of the calendar is listed, so nothing below it may be carried forward
                Log.WriteInformationMessage(LOGTAG, "CalendarNotAvailableForUser", Strings.CalendarNotAvailableForUser(calendar.Id, userId));
                tracker.MarkExaminedSubtree(Path);
                completed = true;
                yield break;
            }
            catch (Exception ex) when (stored != null && stateKey != null && GoogleServiceErrors.IsCalendarSyncTokenExpired(ex))
            {
                // The token is no longer accepted, so start over with a full walk of the calendar
                Log.WriteInformationMessage(LOGTAG, "CalendarSyncTokenExpired", Strings.CalendarSyncTokenExpired(calendar.Id, userId));
                tracker.MarkLoadedStateInvalid(stateKey);
                tracker.MarkExaminedSubtree(Path);
                stored = null;
                request = CreateRequest(null);
                events = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            // The metadata and the ACL share the calendar folder with the events, so when the
            // folder is walked incrementally they must be marked as examined or the
            // carry-forward would duplicate them
            var metadata = new CalendarMetadataSourceEntry(this.Path, calendar);
            if (stored != null)
                tracker.MarkExamined(metadata.Path);
            yield return metadata;

            if (cancellationToken.IsCancellationRequested) yield break;
            if (aclService != null)
            {
                var acl = new CalendarAclSourceEntry(this.Path, calendar.Id, aclService);
                if (stored != null)
                    tracker.MarkExamined(acl.Path);
                yield return acl;
            }

            string? nextSyncToken = null;
            string? nextPageToken = null;
            do
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                if (!string.IsNullOrEmpty(nextPageToken))
                {
                    request.PageToken = nextPageToken;
                    events = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }

                foreach (var evt in events.Items ?? [])
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    if (string.IsNullOrEmpty(evt.Id))
                        continue;

                    var eventPath = Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(this.Path, evt.Id));
                    if (stored != null)
                        tracker.MarkExaminedSubtree(eventPath);

                    // A sync walk reports deleted events as cancelled. A cancelled instance of a
                    // recurring event is an exception record the full listing reports too, so it
                    // is kept; any other cancelled event was deleted
                    if (stored != null && string.Equals(evt.Status, CANCELLED_STATUS, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(evt.RecurringEventId))
                        continue;

                    yield return new CalendarEventSourceEntry(provider, stateKey, this.Path, evt, driveService);
                }

                nextPageToken = events.NextPageToken;
                if (!string.IsNullOrWhiteSpace(events.NextSyncToken))
                    nextSyncToken = events.NextSyncToken;
            } while (!string.IsNullOrEmpty(nextPageToken));

            if (stateKey != null)
            {
                // The key is always stored, with an empty value when there is no token, so the
                // next backup lists the calendar in full
                if (nextSyncToken == null)
                    Log.WriteWarningMessage(LOGTAG, "CalendarSyncTokenMissing", null, Strings.CalendarSyncTokenMissing(calendar.Id, userId));

                tracker.SetState(stateKey, nextSyncToken ?? string.Empty);
            }

            completed = true;
        }
        finally
        {
            // A walk that failed keeps the previous token, so the calendar is carried forward
            // unchanged and the changes are reported again next time; after repeated failures
            // the tracker resets the state so the calendar is listed in full
            if (!completed && stateKey != null)
                tracker.MarkStateFailed(stateKey);
        }
    }

    /// <summary>
    /// Creates the events request. A sync request must carry the same parameters as the full
    /// listing it continues, so neither sets any besides the token.
    /// </summary>
    /// <param name="syncToken">The stored sync token, or <c>null</c> for a full listing</param>
    /// <returns>The request</returns>
    private EventsResource.ListRequest CreateRequest(string? syncToken)
    {
        var request = service.Events.List(calendar.Id);
        if (syncToken != null)
            request.SyncToken = syncToken;
        return request;
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
