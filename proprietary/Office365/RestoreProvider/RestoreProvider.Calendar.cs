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
            var startStr = start.ToGraphTimeString();
            var endStr = end.ToGraphTimeString();

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
            string userIdOrUpn,
            string calendarId,
            GraphEvent original,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var calendar = Uri.EscapeDataString(calendarId);

            // Use a day-ish window to survive all-day/timezone normalization
            var startHint = original.GetStartUtcHint() ?? DateTimeOffset.UtcNow;
            var from = startHint.Date.AddHours(-2).ToGraphTimeString();
            var to = startHint.Date.AddDays(1).AddHours(2).ToGraphTimeString();

            var select = "id,iCalUId,subject,start,end,isAllDay,seriesMasterId,type,organizer,location";

            var url =
                $"{baseUrl}/v1.0/users/{user}/calendars/{calendar}/calendarView" +
                $"?startDateTime={Uri.EscapeDataString(from)}" +
                $"&endDateTime={Uri.EscapeDataString(to)}" +
                $"&$select={Uri.EscapeDataString(select)}" +
                $"&$top={APIHelper.GENERAL_PAGE_SIZE}";

            var results = new List<GraphEvent>();

            await foreach (var ev in provider.GetAllGraphItemsAsync<GraphEvent>(url, ct).ConfigureAwait(false))
            {
                // 1) Exact iCalUId match (best-effort)
                if (!string.IsNullOrEmpty(original.ICalUId) &&
                    string.Equals(ev.ICalUId, original.ICalUId, StringComparison.Ordinal))
                {
                    results.Add(ev);
                    continue;
                }

                // 2) Recurring instances: match by seriesMasterId + originalStart
                if (!string.IsNullOrEmpty(original.SeriesMasterId) &&
                    string.Equals(ev.SeriesMasterId, original.SeriesMasterId, StringComparison.Ordinal) &&
                    original.OriginalStart.HasValue &&
                    ev.OriginalStart.HasValue &&
                    Math.Abs((ev.OriginalStart.Value - original.OriginalStart.Value).TotalMinutes) < 1)
                {
                    results.Add(ev);
                    continue;
                }

                // 3) Series master
                if (original.Type == "seriesMaster" &&
                    ev.Type == "seriesMaster" &&
                    !string.IsNullOrEmpty(original.SeriesMasterId) &&
                    string.Equals(ev.SeriesMasterId, original.SeriesMasterId, StringComparison.Ordinal))
                {
                    results.Add(ev);
                    continue;
                }

                // 4) Last-resort single-instance fallback
                if (original.Type == "singleInstance" &&
                    ev.Type == "singleInstance" &&
                    string.Equals(ev.Subject?.Trim(), original.Subject?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    ev.IsAllDay == original.IsAllDay)
                {
                    results.Add(ev);
                }
            }

            return results;
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
    }
}
