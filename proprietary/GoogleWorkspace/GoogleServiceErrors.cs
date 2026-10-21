// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net;
using System.Text.Json;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google;
using Google.Apis.Requests;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Recognizes the errors Google returns, both the ones that mean a Workspace service is
/// switched off for the user being impersonated, and the ones that pass on their own and
/// should be retried.
/// </summary>
/// <remarks>
/// An administrator may turn Calendar, Tasks or Gmail off for an organizational unit, and
/// archived or suspended accounts lose most services. None of that is an error of the backup,
/// so the service is skipped for that user instead of producing a warning.
///
/// Rate limits are reported inconsistently across the Google APIs. Gmail answers a per-user
/// quota overrun with 403 Forbidden and a reason of <c>rateLimitExceeded</c> or
/// <c>userRateLimitExceeded</c>, other services use 429 Too Many Requests, and the newer
/// error format carries the reason in <c>details</c> as <c>RATE_LIMIT_EXCEEDED</c> next to a
/// status of <c>RESOURCE_EXHAUSTED</c>. All of them refill within a minute, so a request that
/// hit one succeeds when repeated after a pause. A daily quota does not refill for hours, so
/// repeating the request cannot help, and it is deliberately not retried.
/// </remarks>
internal static class GoogleServiceErrors
{
    /// <summary>
    /// The reasons Google attaches to a rate limit that refills within a minute.
    /// </summary>
    private static readonly HashSet<string> RATE_LIMIT_REASONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "rateLimitExceeded",
        "userRateLimitExceeded",
        "quotaExceeded",
        "RATE_LIMIT_EXCEEDED",
        "RESOURCE_EXHAUSTED"
    };

    /// <summary>
    /// The reasons Google attaches to a daily quota, which does not refill for hours.
    /// </summary>
    private static readonly HashSet<string> DAILY_LIMIT_REASONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "dailyLimitExceeded",
        "dailyLimitExceededUnreg"
    };

    /// <summary>
    /// Determines whether the exception means that the given service is not available for
    /// the impersonated user. Only positively identified signatures match, so that missing
    /// scopes, APIs not enabled in the project, and other configuration errors still surface
    /// as warnings.
    /// </summary>
    /// <param name="ex">The exception thrown by the first listing call of the service.</param>
    /// <param name="type">The user service that was being enumerated.</param>
    /// <returns><c>true</c> if the service is switched off for the user; otherwise <c>false</c>.</returns>
    public static bool IsServiceNotAvailableForUser(Exception ex, SourceItemType type)
    {
        if (ex is not GoogleApiException gex)
            return false;

        var reasons = gex.Error?.Errors?.Select(e => e.Reason ?? "") ?? [];
        var message = gex.Message ?? "";

        return type switch
        {
            // Calendar answers with 403 "notACalendarUser" when the service is off for the user.
            SourceItemType.UserCalendar => gex.HttpStatusCode == HttpStatusCode.Forbidden
                && (reasons.Contains("notACalendarUser", StringComparer.OrdinalIgnoreCase)
                    || message.Contains("must be signed up for Google Calendar", StringComparison.OrdinalIgnoreCase)),

            // Gmail answers with 400 "failedPrecondition" and "Mail service not enabled".
            SourceItemType.UserGmail => gex.HttpStatusCode == HttpStatusCode.BadRequest
                && message.Contains("Mail service not enabled", StringComparison.OrdinalIgnoreCase),

            // Tasks answers with a plain 404 when listing the task lists of a user without the
            // service. A user always has a default list, so 404 on the listing cannot mean
            // anything else.
            SourceItemType.UserTasks => gex.HttpStatusCode == HttpStatusCode.NotFound,

            _ => false
        };
    }

    /// <summary>
    /// Determines whether a failed response is worth repeating after a pause: a server error,
    /// an explicit 429, or a 403 that carries one of Google's rate limit signatures. A daily
    /// quota is never retried, whatever status code it arrives with.
    /// </summary>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="reasons">The reason codes found in the error body, if any.</param>
    /// <param name="message">The error message found in the error body, if any.</param>
    /// <returns><c>true</c> if the request should be retried; otherwise <c>false</c>.</returns>
    public static bool IsRetryable(HttpStatusCode statusCode, IEnumerable<string> reasons, string? message)
    {
        var reasonList = reasons.Where(r => !string.IsNullOrEmpty(r)).ToList();
        message ??= "";

        if (reasonList.Any(DAILY_LIMIT_REASONS.Contains) || message.Contains("per day", StringComparison.OrdinalIgnoreCase))
            return false;

        if (statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500)
            return true;

        if (statusCode != HttpStatusCode.Forbidden)
            return false;

        return reasonList.Any(RATE_LIMIT_REASONS.Contains)
            || message.Contains("Quota exceeded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit exceeded", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether an error reported for one request inside a batch is worth repeating
    /// after a pause. The batch itself returns 200, so these errors never reach the HTTP
    /// retry handler and have to be inspected here.
    /// </summary>
    /// <param name="error">The error reported for the individual request.</param>
    /// <returns><c>true</c> if the request should be retried; otherwise <c>false</c>.</returns>
    public static bool IsRetryable(RequestError error)
    {
        var reasons = (error.Errors ?? []).Select(e => e.Reason ?? "").ToList();
        var message = error.Message;

        // The batch parser only fills the structured fields for the legacy error format;
        // fall back to the raw body for the status and details of the newer one.
        if (reasons.Count == 0 && !string.IsNullOrEmpty(error.ErrorResponseContent))
        {
            var (bodyReasons, bodyMessage) = ParseErrorBody(error.ErrorResponseContent);
            reasons = bodyReasons.ToList();
            message ??= bodyMessage;
        }

        return IsRetryable((HttpStatusCode)error.Code, reasons, message);
    }

    /// <summary>
    /// Extracts the reason codes and the message from a Google error body. Both the legacy
    /// format (<c>error.errors[].reason</c>) and the newer one (<c>error.status</c> and
    /// <c>error.details[].reason</c>) are read. A body that is not JSON is returned as the
    /// message, so that text matching still has something to work on.
    /// </summary>
    /// <param name="body">The response body, or <c>null</c> if there was none.</param>
    /// <returns>The reasons found, and the message if any.</returns>
    public static (IReadOnlyList<string> Reasons, string? Message) ParseErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return ([], null);

        var reasons = new List<string>();
        string? message = null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("error", out var error))
                return ([], body);

            if (error.ValueKind == JsonValueKind.String)
                return ([], error.GetString());

            if (error.ValueKind != JsonValueKind.Object)
                return ([], body);

            if (error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                message = messageElement.GetString();

            if (error.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String)
                reasons.Add(statusElement.GetString() ?? "");

            foreach (var listName in new[] { "errors", "details" })
            {
                if (!error.TryGetProperty(listName, out var list) || list.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in list.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                        reasons.Add(reason.GetString() ?? "");
            }
        }
        catch (JsonException)
        {
            return ([], body);
        }

        return (reasons.Where(r => !string.IsNullOrEmpty(r)).ToList(), message);
    }
}
