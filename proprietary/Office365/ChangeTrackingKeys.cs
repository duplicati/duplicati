// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// Builds the keys the provider stores its change tracking state under. A stored token is
/// only valid for the query it was issued for, so when a delta request changes in a way that
/// makes old tokens unusable, change the key prefix of that kind: every stored token is then
/// ignored and the next backup lists in full once.
/// </summary>
internal static class ChangeTrackingKeys
{
    /// <summary>
    /// The prefix of mail folder keys
    /// </summary>
    private const string MAIL_PREFIX = "mail:";

    /// <summary>
    /// The prefix of drive keys
    /// </summary>
    private const string DRIVE_PREFIX = "drive:";

    /// <summary>
    /// The key of a mail folder's delta token. The parent folder is part of the key so a parent
    /// can find the folders it knew in the previous backup, and a folder that is moved to another
    /// parent is walked in full at its new path.
    /// </summary>
    /// <param name="userId">The user id</param>
    /// <param name="parentFolderId">The parent folder id</param>
    /// <param name="folderId">The folder id</param>
    /// <returns>The key</returns>
    public static string MailFolder(string userId, string? parentFolderId, string folderId)
        => MailFolderPrefix(userId, parentFolderId) + folderId;

    /// <summary>
    /// The common prefix of the keys of the mail folders below a parent folder
    /// </summary>
    /// <param name="userId">The user id</param>
    /// <param name="parentFolderId">The parent folder id</param>
    /// <returns>The key prefix</returns>
    public static string MailFolderPrefix(string userId, string? parentFolderId)
        => $"{MAIL_PREFIX}{userId}:{parentFolderId ?? string.Empty}:";

    /// <summary>
    /// The key of a drive's delta token and folder map, stored as one value because the map
    /// only resolves paths correctly for the token it was built with
    /// </summary>
    /// <param name="driveId">The drive id</param>
    /// <returns>The key</returns>
    public static string Drive(string driveId)
        => $"{DRIVE_PREFIX}{driveId}";
}
