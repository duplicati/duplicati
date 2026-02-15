// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.Calendar.v3.Data;

namespace Duplicati.Proprietary.GoogleWorkspace;

partial class RestoreProvider
{
    private CalendarRestoreHelper? _calendarRestoreHelper = null;
    internal CalendarRestoreHelper CalendarRestore => _calendarRestoreHelper ??= new CalendarRestoreHelper(this);

    internal class CalendarRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetUserId = null;
        private string? _targetCalendarId = null;
        private bool _hasLoadedTargetInfo = false;

        public async Task<(string? UserId, string? CalendarId)> GetUserIdAndCalendarTarget(CancellationToken cancel)
        {
            if (_hasLoadedTargetInfo)
            {
                if (string.IsNullOrWhiteSpace(_targetUserId) || string.IsNullOrWhiteSpace(_targetCalendarId))
                    return (null, null);
                return (_targetUserId!, _targetCalendarId!);
            }

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            if (target.Type == SourceItemType.User)
            {
                _targetUserId = target.Metadata.GetValueOrDefault("gsuite:Id");
                if (!string.IsNullOrWhiteSpace(_targetUserId))
                    _targetCalendarId = await GetDefaultRestoreTargetCalendar(_targetUserId, cancel);
            }
            else if (target.Type == SourceItemType.UserCalendar)
            {
                _targetUserId = target.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(_targetUserId))
                    _targetCalendarId = await GetDefaultRestoreTargetCalendar(_targetUserId, cancel);
            }
            else if (target.Type == SourceItemType.Calendar)
            {
                _targetUserId = target.Metadata.GetValueOrDefault("gsuite:UserId");
                if (string.IsNullOrWhiteSpace(_targetUserId))
                    _targetUserId = target.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
                _targetCalendarId = target.Metadata.GetValueOrDefault("gsuite:Id");
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreCalendarInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring calendar events.");
            }

            _hasLoadedTargetInfo = true;

            if (string.IsNullOrWhiteSpace(_targetUserId) || string.IsNullOrWhiteSpace(_targetCalendarId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreCalendarMissingIds", null, $"Missing target userId or calendarId for restoring calendar events.");
                return (null, null);
            }

            return (_targetUserId, _targetCalendarId);
        }

        private async Task<string> GetDefaultRestoreTargetCalendar(string userId, CancellationToken cancel)
        {
            const string RESTORED_CALENDAR_NAME = "Restored";

            var calendarService = Provider._apiHelper.GetCalendarService(userId);

            // Check if calendar already exists
            var calendars = await calendarService.CalendarList.List().ExecuteAsync(cancel);
            var existingCalendar = calendars.Items?.FirstOrDefault(c =>
                c.Summary?.Equals(RESTORED_CALENDAR_NAME, StringComparison.OrdinalIgnoreCase) == true);

            if (existingCalendar != null)
                return existingCalendar.Id;

            // Create new calendar
            var newCalendar = new Calendar
            {
                Summary = RESTORED_CALENDAR_NAME,
                TimeZone = "UTC"
            };

            var createdCalendar = await calendarService.Calendars.Insert(newCalendar).ExecuteAsync(cancel);
            return createdCalendar.Id;
        }

        public async Task<string?> CreateEvent(string userId, string calendarId, Event eventItem, CancellationToken cancel)
        {
            var calendarService = Provider._apiHelper.GetCalendarService(userId);

            // Check for duplicates by iCalUID if available
            if (!Provider._ignoreExisting && !string.IsNullOrWhiteSpace(eventItem.ICalUID))
            {
                var existingEvents = await calendarService.Events.List(calendarId).ExecuteAsync(cancel);
                var duplicate = existingEvents.Items?.FirstOrDefault(e =>
                    e.ICalUID?.Equals(eventItem.ICalUID, StringComparison.Ordinal) == true);

                if (duplicate != null)
                {
                    Log.WriteInformationMessage(LOGTAG, "CreateEventSkipDuplicate", $"Event with ICalUID {eventItem.ICalUID} already exists, skipping.");
                    return duplicate.Id;
                }
            }

            // Clean up properties that shouldn't be sent on creation
            eventItem.Id = null;
            eventItem.ETag = null;
            eventItem.HtmlLink = null;
            eventItem.CreatedRaw = null;
            eventItem.UpdatedRaw = null;

            var createdEvent = await calendarService.Events.Insert(eventItem, calendarId).ExecuteAsync(cancel);
            return createdEvent.Id;
        }

        public async Task AddAttachment(string userId, string calendarId, string eventId, string fileName, Stream contentStream, string mimeType, CancellationToken cancel)
        {
            var calendarService = Provider._apiHelper.GetCalendarService(userId);

            // Google Calendar API doesn't support direct attachment uploads
            // Attachments need to be stored in Drive and linked
            // For now, we log a warning
            Log.WriteWarningMessage(LOGTAG, "CalendarAttachmentsNotSupported", null, $"Calendar attachments are not directly supported in Google Calendar API. File: {fileName}");
        }

        public async Task RestoreAcls(string userId, string calendarId, Stream aclStream, CancellationToken cancel)
        {
            var calendarService = Provider._apiHelper.GetCalendarService(userId);

            // Deserialize the ACL list
            Acl? acl;
            try
            {
                acl = await JsonSerializer.DeserializeAsync<Acl>(aclStream, cancellationToken: cancel);
            }
            catch
            {
                return;
            }

            if (acl?.Items == null || acl.Items.Count == 0)
                return;

            foreach (var aclRule in acl.Items)
            {
                try
                {
                    // Skip owner permissions (can't change ownership easily)
                    if (aclRule.Role == "owner")
                        continue;

                    // Create a new ACL rule without the ID and ETag (these are assigned by Google)
                    var newAclRule = new AclRule
                    {
                        Role = aclRule.Role,
                        Scope = aclRule.Scope != null ? new AclRule.ScopeData
                        {
                            Type = aclRule.Scope.Type,
                            Value = aclRule.Scope.Value
                        } : null
                    };

                    await calendarService.Acl.Insert(newAclRule, calendarId).ExecuteAsync(cancel);
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreCalendarAclFailed", ex, $"Failed to restore ACL rule with role {aclRule.Role} for calendar {calendarId}");
                }
            }
        }
    }

    private async Task RestoreCalendarEvents(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var events = GetMetadataByType(SourceItemType.CalendarEvent);
        if (events.Count == 0)
            return;

        (var userId, var calendarId) = await CalendarRestore.GetUserIdAndCalendarTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(calendarId))
            return;

        // Group attachments by event path
        var attachments = GetMetadataByType(SourceItemType.CalendarEventAttachment)
            .GroupBy(k => Util.AppendDirSeparator(Path.GetDirectoryName(k.Key.TrimEnd(Path.DirectorySeparatorChar)) ?? ""))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var eventItem in events)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = eventItem.Key;
                var contentPath = SystemIO.IO_OS.PathCombine(originalPath, "content.json");
                var icsPath = SystemIO.IO_OS.PathCombine(originalPath, "event.ics");
                var contentEntry = _temporaryFiles.GetValueOrDefault(contentPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreCalendarEventsMissingContent", null, $"Missing content for event {originalPath}, skipping.");
                    continue;
                }

                Event? calendarEvent;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    calendarEvent = await JsonSerializer.DeserializeAsync<Event>(contentStream, cancellationToken: cancel);
                }

                if (calendarEvent == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreCalendarEventsInvalidContent", null, $"Invalid content for event {originalPath}, skipping.");
                    continue;
                }

                var newEventId = await CalendarRestore.CreateEvent(userId, calendarId, calendarEvent, cancel);

                // Restore attachments if any
                var eventPathWithSep = Util.AppendDirSeparator(originalPath);
                if (newEventId != null && attachments.TryGetValue(eventPathWithSep, out var eventAttachments))
                {
                    foreach (var att in eventAttachments)
                    {
                        try
                        {
                            var attPath = att.Key;
                            var attMetadata = att.Value;
                            var name = attMetadata.GetValueOrDefault("gsuite:Name") ?? "attachment";
                            var mimeType = attMetadata.GetValueOrDefault("gsuite:MimeType") ?? "application/octet-stream";

                            if (_temporaryFiles.TryRemove(attPath, out var attContent))
                            {
                                using var attStream = SystemIO.IO_OS.FileOpenRead(attContent);
                                await CalendarRestore.AddAttachment(userId, calendarId, newEventId, name, attStream, mimeType, cancel);
                                _metadata.TryRemove(attPath, out _);
                                attContent?.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.WriteWarningMessage(LOGTAG, "RestoreCalendarAttachmentFailed", ex, $"Failed to restore attachment for event {originalPath}");
                        }
                    }
                }

                _metadata.TryRemove(originalPath, out _);
                _metadata.TryRemove(contentPath, out _);
                _temporaryFiles.TryRemove(contentPath, out var cFile);
                cFile?.Dispose();

                // We do not use the ICS file for restore, so we can remove it
                _metadata.TryRemove(icsPath, out _);
                _temporaryFiles.TryRemove(icsPath, out var iFile);
                iFile?.Dispose();

            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreCalendarEventsFailed", ex, $"Failed to restore event {eventItem.Key}");
            }
        }
    }

    private async Task RestoreCalendarAcls(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var aclEntries = GetMetadataByType(SourceItemType.CalendarEventACL);
        if (aclEntries.Count == 0)
            return;

        (var userId, var calendarId) = await CalendarRestore.GetUserIdAndCalendarTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(calendarId))
            return;

        foreach (var aclEntry in aclEntries)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = aclEntry.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreCalendarAclsMissingContent", null, $"Missing content for ACL {originalPath}, skipping.");
                    continue;
                }

                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    await CalendarRestore.RestoreAcls(userId, calendarId, contentStream, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreCalendarAclsFailed", ex, $"Failed to restore ACLs for {aclEntry.Key}");
            }
        }
    }
}
