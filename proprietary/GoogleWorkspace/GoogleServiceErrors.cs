// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Recognizes the errors Google returns when a Workspace service is switched off for the
/// user being impersonated, as opposed to a misconfiguration of the credentials or the
/// project. An administrator may turn Calendar, Tasks or Gmail off for an organizational unit,
/// and archived or suspended accounts lose most services. None of that is an error of the
/// backup, so the service is skipped for that user instead of producing a warning.
/// </summary>
internal static class GoogleServiceErrors
{
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
}
