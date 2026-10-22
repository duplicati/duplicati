// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Builds the keys the provider stores its change tracking state under. A stored token is
/// only valid for the request it was issued for, so when a request changes in a way that
/// makes old tokens unusable, change the key prefix of that kind: every stored token is then
/// ignored and the next backup lists in full once.
/// </summary>
internal static class ChangeTrackingKeys
{
    /// <summary>
    /// The prefix of Gmail keys
    /// </summary>
    private const string GMAIL_PREFIX = "gmail:";

    /// <summary>
    /// The prefix of drive keys
    /// </summary>
    private const string DRIVE_PREFIX = "drive:";

    /// <summary>
    /// The prefix of calendar keys
    /// </summary>
    private const string CALENDAR_PREFIX = "calendar:";

    /// <summary>
    /// The prefix of contacts keys
    /// </summary>
    private const string CONTACTS_PREFIX = "contacts:";

    /// <summary>
    /// The drive id segment of the My Drive key. The user's own drive has no drive id of its
    /// own in the changes feed, and the entry path uses the <c>root</c> alias for it
    /// </summary>
    private const string MY_DRIVE = "root";

    /// <summary>
    /// The key of a mailbox's history id. One history covers every label of the mailbox.
    /// </summary>
    /// <param name="userId">The user id</param>
    /// <returns>The key</returns>
    public static string Gmail(string userId)
        => GMAIL_PREFIX + userId;

    /// <summary>
    /// The key of the change token and folder map of a user's My Drive
    /// </summary>
    /// <param name="userId">The user id</param>
    /// <returns>The key</returns>
    public static string MyDrive(string userId)
        => $"{DRIVE_PREFIX}{userId}:{MY_DRIVE}";

    /// <summary>
    /// The key of the change token and folder map of a shared drive. The drive is listed once
    /// per user that can see it and once at the tenant level, and each listing keeps its own
    /// token, as what it lists depends on the access of the account doing the listing.
    /// </summary>
    /// <param name="userId">The user the drive is listed through, or <c>null</c> at the tenant level</param>
    /// <param name="driveId">The drive id</param>
    /// <returns>The key</returns>
    public static string SharedDrive(string? userId, string driveId)
        => $"{DRIVE_PREFIX}{userId ?? string.Empty}:{driveId}";

    /// <summary>
    /// The key of a calendar's sync token
    /// </summary>
    /// <param name="userId">The user id</param>
    /// <param name="calendarId">The calendar id</param>
    /// <returns>The key</returns>
    public static string Calendar(string userId, string calendarId)
        => $"{CALENDAR_PREFIX}{userId}:{calendarId}";

    /// <summary>
    /// The key of the sync token of a user's contacts
    /// </summary>
    /// <param name="userId">The user id</param>
    /// <returns>The key</returns>
    public static string Contacts(string userId)
        => CONTACTS_PREFIX + userId;
}
