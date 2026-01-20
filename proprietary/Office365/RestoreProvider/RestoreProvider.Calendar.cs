// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Json;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

partial class RestoreProvider
{
    internal CalendarApiImpl CalendarApi => new CalendarApiImpl(_apiHelper);
    private CalendarRestoreHelper? _calendarRestoreHelper = null;
    internal CalendarRestoreHelper CalendarRestore => _calendarRestoreHelper ??= new CalendarRestoreHelper(this);

    internal class CalendarApiImpl(APIHelper provider)
    {
        public async Task RestoreCalendarEventToCalendarAsync(
            string userId,
            string targetCalendarId,
            Stream eventJsonStream,
            CancellationToken cancellationToken)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var calendar = Uri.EscapeDataString(targetCalendarId);

            // Create event in a specific calendar:
            // POST /users/{id}/calendars/{id}/events  (application/json event body)
            var url = $"{baseUrl}/v1.0/users/{user}/calendars/{calendar}/events";

            async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new System.Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);

                // Important: create new content per attempt
                eventJsonStream.Position = 0;
                req.Content = new StreamContent(eventJsonStream);
                req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                cancellationToken).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);
        }

        public async Task<GraphCalendar> CreateCalendarAsync(
            string userId,
            string name,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var url = $"{baseUrl}/v1.0/users/{user}/calendars";

            var body = new GraphCreateCalendarRequest { Name = name };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
                => new HttpRequestMessage(HttpMethod.Post, new System.Uri(url))
                {
                    Headers =
                    {
                        Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false)
                    },
                    Content = JsonContent.Create(body)
                };

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphCalendar>(respStream, cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the created calendar id.");

            return created;
        }

        public async Task<GraphCalendar?> GetCalendarByNameAsync(
            string userId,
            string name,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var filterName = name.Replace("'", "''");

            var url = $"{baseUrl}/v1.0/users/{user}/calendars?$filter=name eq '{filterName}'&$top=1";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new System.Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GraphPage<GraphCalendar>>(stream, cancellationToken: ct).ConfigureAwait(false);

            return result?.Value?.FirstOrDefault();
        }

        public async Task<GraphEvent> CreateCalendarEventAsync(
            string userId,
            string calendarId,
            GraphEvent eventItem,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var calendar = Uri.EscapeDataString(calendarId);
            var url = $"{baseUrl}/v1.0/users/{user}/calendars/{calendar}/events";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
                => new HttpRequestMessage(HttpMethod.Post, new System.Uri(url))
                {
                    Headers =
                    {
                        Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false)
                    },
                    Content = JsonContent.Create(eventItem)
                };

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<GraphEvent>(respStream, cancellationToken: ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to deserialize created event");
        }

        public async Task<GraphEvent> UpdateCalendarEventAsync(
            string userId,
            string eventId,
            GraphEvent eventUpdate,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var url = $"{baseUrl}/v1.0/users/{user}/events/{eventId}";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
                => new HttpRequestMessage(HttpMethod.Patch, new System.Uri(url))
                {
                    Headers =
                    {
                        Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false)
                    },
                    Content = JsonContent.Create(eventUpdate)
                };

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<GraphEvent>(respStream, cancellationToken: ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to deserialize updated event");
        }

        public async Task<List<GraphEvent>> GetCalendarEventInstancesAsync(
            string userId,
            string eventId,
            DateTimeOffset start,
            DateTimeOffset end,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            // Format dates as ISO 8601
            var startStr = start.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endStr = end.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var url = $"{baseUrl}/v1.0/users/{user}/events/{eventId}/instances?startDateTime={startStr}&endDateTime={endStr}";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new System.Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<GraphPage<GraphEvent>>(stream, cancellationToken: ct).ConfigureAwait(false);
            return page?.Value ?? [];
        }

        public async Task<List<GraphEvent>> FindEventsAsync(
            string userId,
            string calendarId,
            string subject,
            DateTimeOffset start,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var calendar = Uri.EscapeDataString(calendarId);

            // Filter by subject and start time range (e.g. +/- 1 minute to account for precision issues)
            var startStr = start.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endStr = start.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var subjectFilter = subject.Replace("'", "''");

            var url = $"{baseUrl}/v1.0/users/{user}/calendars/{calendar}/events?$filter=subject eq '{subjectFilter}' and start/dateTime ge '{startStr}' and start/dateTime le '{endStr}'";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new System.Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<GraphPage<GraphEvent>>(stream, cancellationToken: ct).ConfigureAwait(false);
            return page?.Value ?? [];
        }

        public async Task AddAttachmentAsync(
            string userId,
            string calendarId,
            string eventId,
            string name,
            string contentType,
            Stream contentStream,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var cal = Uri.EscapeDataString(calendarId);
            var ev = Uri.EscapeDataString(eventId);

            // Check size
            if (contentStream.Length > 3 * 1024 * 1024) // 3MB limit for simple upload
            {
                // Large file upload
                var url = $"{baseUrl}/v1.0/users/{user}/calendars/{cal}/events/{ev}/attachments/createUploadSession";
                var sessionBody = new { AttachmentItem = new { attachmentType = "file", name = name, size = contentStream.Length } };

                async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, new System.Uri(url));
                    req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                    req.Content = JsonContent.Create(sessionBody);
                    return req;
                }

                using var resp = await provider.SendWithRetryShortAsync(
                    requestFactory,
                    ct).ConfigureAwait(false);

                await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

                using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var session = await JsonSerializer.DeserializeAsync<GraphUploadSession>(respStream, cancellationToken: ct).ConfigureAwait(false);

                if (session?.UploadUrl != null)
                {
                    await provider.UploadFileToSessionAsync(session.UploadUrl, contentStream, ct).ConfigureAwait(false);
                }
            }
            else
            {
                // Simple upload
                var url = $"{baseUrl}/v1.0/users/{user}/calendars/{cal}/events/{ev}/attachments";

                using var ms = new MemoryStream();
                await contentStream.CopyToAsync(ms, ct);
                var bytes = ms.ToArray();
                var base64 = Convert.ToBase64String(bytes);

                var body = new
                {
                    ODataType = "#microsoft.graph.fileAttachment",
                    Name = name,
                    ContentType = contentType,
                    ContentBytes = base64
                };

                async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, new System.Uri(url));
                    req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                    req.Content = JsonContent.Create(body);
                    return req;
                }

                using var resp = await provider.SendWithRetryShortAsync(
                    requestFactory,
                    ct).ConfigureAwait(false);

                await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
            }
        }
    }

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
                _targetUserId = target.Metadata["o365:Id"]!;
                _targetCalendarId = await GetDefaultRestoreTargetCalendar(_targetUserId, cancel);
            }
            else if (target.Type == SourceItemType.UserCalendar || target.Type == SourceItemType.Calendar)
            {
                _targetUserId = target.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
                _targetCalendarId = target.Metadata["o365:Id"];
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreCalendarEventsInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring calendar events.");
            }

            _hasLoadedTargetInfo = true;

            if (string.IsNullOrWhiteSpace(_targetUserId) || string.IsNullOrWhiteSpace(_targetCalendarId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreCalendarEventsMissingIds", null, $"Missing target userId or calendarId for restoring calendar events.");
                return (null, null);
            }

            return (_targetUserId, _targetCalendarId);
        }

        private async Task<string> GetDefaultRestoreTargetCalendar(string userId, CancellationToken cancel)
        {
            const string RESTORED_CALENDAR_NAME = "Restored";

            var existing = await Provider.CalendarApi.GetCalendarByNameAsync(userId, RESTORED_CALENDAR_NAME, cancel);
            if (existing != null)
                return existing.Id;

            var created = await Provider.CalendarApi.CreateCalendarAsync(userId, RESTORED_CALENDAR_NAME, cancel);
            return created.Id;
        }

        public async Task RestoreEvents(List<KeyValuePair<string, Dictionary<string, string?>>> events, CancellationToken cancel)
        {
            (var userId, var calendarId) = await GetUserIdAndCalendarTarget(cancel);
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(calendarId))
                return;

            // Group attachments by event path
            var attachments = Provider.GetMetadataByType(SourceItemType.CalendarEventAttachment)
                .GroupBy(k => Util.AppendDirSeparator(Path.GetDirectoryName(k.Key.TrimEnd(Path.DirectorySeparatorChar))))
                .ToDictionary(g => g.Key, g => g.ToList());

            var singles = new List<(string Path, GraphEvent Event)>();
            var masters = new List<(string Path, GraphEvent Event)>();
            var exceptions = new List<(string Path, GraphEvent Event)>();

            foreach (var eventItem in events)
            {
                if (cancel.IsCancellationRequested) break;

                var originalPath = eventItem.Key;
                GraphEvent? graphEvent = null;

                try
                {
                    // Try to read as file (old format)
                    if (await Provider.FileExists(originalPath, cancel))
                    {
                        using var stream = await Provider.OpenRead(originalPath, cancel);
                        graphEvent = await JsonSerializer.DeserializeAsync<GraphEvent>(stream, cancellationToken: cancel);
                    }
                    // Try to read content.json (new format)
                    else
                    {
                        var contentPath = SystemIO.IO_OS.PathCombine(originalPath, "content.json");
                        if (await Provider.FileExists(contentPath, cancel))
                        {
                            using var stream = await Provider.OpenRead(contentPath, cancel);
                            graphEvent = await JsonSerializer.DeserializeAsync<GraphEvent>(stream, cancellationToken: cancel);

                            // Clean up content file metadata
                            Provider._metadata.TryRemove(contentPath, out _);
                        }
                    }

                    if (graphEvent == null) continue;

                    if (graphEvent.Type == "seriesMaster")
                        masters.Add((originalPath, graphEvent));
                    else if (graphEvent.Type == "exception")
                        exceptions.Add((originalPath, graphEvent));
                    else if (graphEvent.Type == "occurrence")
                        continue; // Skip occurrences
                    else
                        singles.Add((originalPath, graphEvent));
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "RestoreCalendarEventAnalysisFailed", ex, $"Failed to analyze event {originalPath}: {ex.Message}");
                }
            }

            // Restore singles
            foreach (var item in singles)
            {
                await RestoreSingleEvent(userId, calendarId, item.Path, item.Event, attachments, cancel);
            }

            // Restore masters
            var masterMap = new Dictionary<string, string>(); // OldId -> NewId
            foreach (var item in masters)
            {
                var newId = await RestoreMasterEvent(userId, calendarId, item.Path, item.Event, attachments, cancel);
                if (newId != null)
                    masterMap[item.Event.Id] = newId;
            }

            // Restore exceptions
            foreach (var item in exceptions)
            {
                await RestoreExceptionEvent(userId, calendarId, item.Path, item.Event, masterMap, attachments, cancel);
            }
        }

        private async Task RestoreAttachments(string userId, string calendarId, string eventId, string eventPath, Dictionary<string, List<KeyValuePair<string, Dictionary<string, string?>>>> attachments, CancellationToken cancel)
        {
            var eventPathWithSep = Util.AppendDirSeparator(eventPath);
            if (!attachments.TryGetValue(eventPathWithSep, out var eventAttachments))
                return;

            foreach (var att in eventAttachments)
            {
                try
                {
                    var attPath = att.Key;
                    var metadata = att.Value;
                    var name = metadata.GetValueOrDefault("o365:Name") ?? "attachment";
                    var contentType = metadata.GetValueOrDefault("o365:ContentType") ?? "application/octet-stream";

                    using var stream = await Provider.OpenRead(attPath, cancel);
                    await Provider.CalendarApi.AddAttachmentAsync(userId, calendarId, eventId, name, contentType, stream, cancel);

                    Provider._metadata.TryRemove(attPath, out _);
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "RestoreCalendarAttachmentFailed", ex, $"Failed to restore attachment for event {eventPath}: {ex.Message}");
                }
            }
        }

        private async Task RestoreSingleEvent(string userId, string calendarId, string path, GraphEvent eventItem, Dictionary<string, List<KeyValuePair<string, Dictionary<string, string?>>>> attachments, CancellationToken cancel)
        {
            try
            {
                if (!Provider._overwrite && !string.IsNullOrWhiteSpace(eventItem.Subject))
                {
                    DateTimeOffset? start = null;
                    if (eventItem.Start is JsonElement startElem && startElem.ValueKind == JsonValueKind.Object)
                    {
                        if (startElem.TryGetProperty("dateTime", out var dtProp) && dtProp.GetString() is string dtStr)
                        {
                            if (DateTimeOffset.TryParse(dtStr, out var dt))
                            {
                                start = dt;
                            }
                        }
                    }

                    if (start.HasValue)
                    {
                        var existing = await Provider.CalendarApi.FindEventsAsync(userId, calendarId, eventItem.Subject, start.Value, cancel);
                        if (existing.Count > 0)
                        {
                            Log.WriteInformationMessage(LOGTAG, "RestoreSingleEventSkipDuplicate", null, $"Skipping duplicate event {path} (Subject: {eventItem.Subject})");
                            Provider._metadata.TryRemove(path, out _);
                            return;
                        }
                    }
                }

                // Clean up properties that shouldn't be sent on creation
                eventItem.Id = "";
                eventItem.CreatedDateTime = null;
                eventItem.LastModifiedDateTime = null;

                var created = await Provider.CalendarApi.CreateCalendarEventAsync(userId, calendarId, eventItem, cancel);
                await RestoreAttachments(userId, calendarId, created.Id, path, attachments, cancel);
                Provider._metadata.TryRemove(path, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreSingleEventFailed", ex, $"Failed to restore event {path}: {ex.Message}");
            }
        }

        private async Task<string?> RestoreMasterEvent(string userId, string calendarId, string path, GraphEvent eventItem, Dictionary<string, List<KeyValuePair<string, Dictionary<string, string?>>>> attachments, CancellationToken cancel)
        {
            try
            {
                if (!Provider._overwrite && !string.IsNullOrWhiteSpace(eventItem.Subject))
                {
                    DateTimeOffset? start = null;
                    if (eventItem.Start is JsonElement startElem && startElem.ValueKind == JsonValueKind.Object)
                    {
                        if (startElem.TryGetProperty("dateTime", out var dtProp) && dtProp.GetString() is string dtStr)
                        {
                            if (DateTimeOffset.TryParse(dtStr, out var dt))
                            {
                                start = dt;
                            }
                        }
                    }

                    if (start.HasValue)
                    {
                        var existing = await Provider.CalendarApi.FindEventsAsync(userId, calendarId, eventItem.Subject, start.Value, cancel);
                        if (existing.Count > 0)
                        {
                            Log.WriteInformationMessage(LOGTAG, "RestoreMasterEventSkipDuplicate", null, $"Skipping duplicate master event {path} (Subject: {eventItem.Subject})");
                            Provider._metadata.TryRemove(path, out _);
                            // Return the ID of the existing event so exceptions can be attached to it?
                            // If we skip it, we should probably return the existing ID.
                            return existing[0].Id;
                        }
                    }
                }

                eventItem.Id = "";
                eventItem.CreatedDateTime = null;
                eventItem.LastModifiedDateTime = null;

                var created = await Provider.CalendarApi.CreateCalendarEventAsync(userId, calendarId, eventItem, cancel);
                await RestoreAttachments(userId, calendarId, created.Id, path, attachments, cancel);
                Provider._metadata.TryRemove(path, out _);
                return created.Id;
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreMasterEventFailed", ex, $"Failed to restore master event {path}: {ex.Message}");
                return null;
            }
        }

        private async Task RestoreExceptionEvent(string userId, string calendarId, string path, GraphEvent eventItem, Dictionary<string, string> masterMap, Dictionary<string, List<KeyValuePair<string, Dictionary<string, string?>>>> attachments, CancellationToken cancel)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(eventItem.SeriesMasterId) || !masterMap.TryGetValue(eventItem.SeriesMasterId, out var newMasterId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreExceptionEventMissingMaster", null, $"Could not find restored master for exception event {path}, skipping.");
                    return;
                }

                if (eventItem.OriginalStart == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreExceptionEventMissingOriginalStart", null, $"Missing original start time for exception event {path}, skipping.");
                    return;
                }

                // Find the occurrence
                // We search for instances around the original start time
                var searchStart = eventItem.OriginalStart.Value.AddMinutes(-1);
                var searchEnd = eventItem.OriginalStart.Value.AddMinutes(1);

                var instances = await Provider.CalendarApi.GetCalendarEventInstancesAsync(userId, newMasterId, searchStart, searchEnd, cancel);

                var instance = instances.FirstOrDefault(i =>
                {
                    // Parse start time from instance
                    if (i.Start is JsonElement startElem && startElem.ValueKind == JsonValueKind.Object)
                    {
                        if (startElem.TryGetProperty("dateTime", out var dtProp) && dtProp.GetString() is string dtStr)
                        {
                            if (DateTimeOffset.TryParse(dtStr, out var dt))
                            {
                                return Math.Abs((dt - eventItem.OriginalStart.Value).TotalSeconds) < 5;
                            }
                        }
                    }
                    return false;
                });

                if (instance == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreExceptionEventInstanceNotFound", null, $"Could not find occurrence instance for exception event {path}, skipping.");
                    return;
                }

                // Update the instance
                eventItem.Id = "";
                eventItem.SeriesMasterId = null;
                eventItem.Type = null;
                eventItem.CreatedDateTime = null;
                eventItem.LastModifiedDateTime = null;
                eventItem.OriginalStart = null;

                var updated = await Provider.CalendarApi.UpdateCalendarEventAsync(userId, instance.Id, eventItem, cancel);
                await RestoreAttachments(userId, calendarId, updated.Id, path, attachments, cancel);
                Provider._metadata.TryRemove(path, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreExceptionEventFailed", ex, $"Failed to restore exception event {path}: {ex.Message}");
            }
        }
    }
}
