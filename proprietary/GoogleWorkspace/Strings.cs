// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Localization.Short;

namespace Duplicati.Proprietary.GoogleWorkspace;

internal static class Strings
{
    internal static class Options
    {
        public static string GoogleClientIdShort => LC.L("The Google Client ID");
        public static string GoogleClientIdLong => LC.L("The Client ID for the Google App.");
        public static string GoogleClientSecretShort => LC.L("The Google Client Secret");
        public static string GoogleClientSecretLong => LC.L("The Client Secret for the Google App.");
        public static string GoogleRefreshTokenShort => LC.L("The Google Refresh Token");
        public static string GoogleRefreshTokenLong => LC.L("The Refresh Token for the Google App.");
        public static string GoogleServiceAccountJsonShort => LC.L("The Google Service Account JSON");
        public static string GoogleServiceAccountJsonLong => LC.L("The Google Service Account JSON content.");
        public static string GoogleServiceAccountFileShort => LC.L("The Google Service Account File");
        public static string GoogleServiceAccountFileLong => LC.L("The path to the Google Service Account JSON file.");
        public static string GoogleAdminEmailShort => LC.L("The Google Admin Email");
        public static string GoogleAdminEmailLong => LC.L("The email of the Google Admin user to impersonate.");
        public static string GoogleIncludedRootTypesShort => LC.L("Included Root Types");
        public static string GoogleIncludedRootTypesLong => LC.L("Comma-separated list of root types to include (e.g. Users, Groups, SharedDrives).");
        public static string GoogleIncludedUserTypesShort => LC.L("Included User Types");
        public static string GoogleIncludedUserTypesLong => LC.L("Comma-separated list of user types to include (e.g. Gmail, Drive, Calendar).");
        public static string GoogleIgnoreExistingOptionShort => LC.L("Ignore existing items");
        public static string GoogleIgnoreExistingOptionLong => LC.L("If set, existing items in the destination will not be overwritten.");
        public static string GoogleAvoidCalendarAclOptionShort => LC.L("Avoid reading calendar ACLs");
        public static string GoogleAvoidCalendarAclOptionLong => LC.L("Calendar ACLs require write permissions to be able to read them. If the account does not have write permissions, this option can be used to avoid reading them.");
        public static string GoogleExcludeArchivedUsersShort => LC.L("Exclude archived users");
        public static string GoogleExcludeArchivedUsersLong => LC.L("Archived user accounts are backed up by default, as their Gmail and Drive data remain readable and they do not consume a license seat. Enable this option to leave archived users out of the backup.");
        public static string GoogleExcludeSuspendedUsersShort => LC.L("Exclude suspended users");
        public static string GoogleExcludeSuspendedUsersLong => LC.L("Suspended user accounts are backed up by default and do not consume a license seat. Enable this option to leave suspended users out of the backup.");
        public static string GoogleDisableGmailDeltaShort => LC.L("Disable the incremental listing of mailboxes");
        public static string GoogleDisableGmailDeltaLong => LC.L("By default a mailbox is listed from the Gmail history, storing the history id of the mailbox with each backup, so that later backups fetch only the messages that were created, changed, relabeled or deleted since the previous backup and carry the rest forward from it. The first backup, the first backup after the local database is recreated, the backup after one that was stopped, and a backup more than about a week after the previous one still list every mailbox in full, and the file count shown while the backup runs is that of the previous backup. Set this option to list every mailbox in full on every backup instead.");
        public static string GoogleDisableDriveDeltaShort => LC.L("Disable the incremental listing of drives");
        public static string GoogleDisableDriveDeltaLong => LC.L("By default My Drive and shared drives are listed from the Drive changes feed, storing a page token and a folder map per drive with each backup, so that later backups list only the files and folders that were created, changed, moved or removed since the previous backup and carry the rest forward from it, replacing the request per folder and the permission, comment and revision requests per file with a few requests per drive. The first backup, the first backup after the local database is recreated, and the backup after one that was stopped still list every drive in full, and the file count shown while the backup runs is that of the previous backup. Set this option to list every drive in full on every backup instead.");
        public static string GoogleDisableCalendarDeltaShort => LC.L("Disable the incremental listing of calendars");
        public static string GoogleDisableCalendarDeltaLong => LC.L("By default calendars are listed with a sync token stored per calendar with each backup, so that later backups list only the events that were created, changed or deleted since the previous backup and carry the rest forward from it. The first backup, the first backup after the local database is recreated, and the backup after one that was stopped still list every calendar in full. Set this option to list every calendar in full on every backup instead.");
        public static string GoogleDisableContactsDeltaShort => LC.L("Disable the incremental listing of contacts");
        public static string GoogleDisableContactsDeltaLong => LC.L("By default contacts are listed with a sync token stored per user with each backup, so that later backups list only the contacts that were created, changed or deleted since the previous backup and carry the rest forward from it. The first backup, the first backup after the local database is recreated, the backup after one that was stopped, and a backup more than a week after the previous one still list every user's contacts in full. Set this option to list contacts in full on every backup instead.");
        public static string GoogleDriveDeltaMaxItemsShort => LC.L("Maximum number of changes to read from a drive");
        public static string GoogleDriveDeltaMaxItemsLong => LC.L("The changes of a drive are held in memory while the backup resolves their paths. If a drive reports more changes than this, for instance after a migration, the changes are discarded and the drive is listed in full instead.");
    }

    internal static class Common
    {
        public static string DisplayName => LC.L("Google Workspace");
        public static string Description => LC.L("Backup and restore Google Workspace data.");
        public static string WebModuleDisplayName => LC.L("Google Workspace Web Module");
        public static string WebModuleDescription => LC.L("Web module for Google Workspace.");
        public static string WebModuleOperationShort => LC.L("Operation");
        public static string WebModuleOperationLong => LC.L("The operation to perform.");
        public static string WebModuleURLShort => LC.L("URL");
        public static string WebModuleURLLong => LC.L("The URL to connect to.");
        public static string WebModulePathShort => LC.L("Path");
        public static string WebModulePathLong => LC.L("The path to list.");
    }

    internal static class CalendarAclSourceEntry
    {
        public static string ForbiddenAccessError(string optionname, string scopename) => LC.L($"Access to calendar ACLs was denied. Use the --{optionname} option to avoid reading them or grant the scope {scopename}.");
    }

    public static string MetadataStorageNotEnabled(string optionname) => LC.L(
        $"Storing metadata content in the database must be enabled to use the Google Workspace source provider. Use the option: --{optionname}");

    public static string RestoreTargetMissingOverwriteOption(string originalOptionName, string alternateOptionName) => LC.L($"The Google Workspace restore target must have the --{originalOptionName} option set to true. Use the provider specific option --{alternateOptionName} to avoid overwriting existing items.");

    public static string InvalidRestoreTargetType(string? type) => LC.L($"Invalid restore target type: {type}");
    public static string RestoreTargetNotFound(string path) => LC.L($"Restore target path not found: {path}");
    public static string SkippingExcludedUser(string email, string state, string optionname) => LC.L($"Skipping {state} user {email} as requested by the option --{optionname}");
    public static string SkippedExcludedUsers(int count, string state, string optionname) => LC.L($"Skipped {count} {state} user(s) as requested by the option --{optionname}");
    public static string ContactPhotosSkippedForInactiveUser(string contactId) => LC.L($"The photos of contact {contactId} are not available through the suspended or archived account, skipping them");
    public static string CommentsNotReadableForInactiveUser(string fileId) => LC.L($"The comments of file {fileId} are not readable through the suspended or archived account, recording an empty comment list");
    public static string CalendarNotAvailableForUser(string calendarId, string userId) => LC.L($"The calendar {calendarId} in the calendar list of user {userId} cannot be read because its owner has no Google Calendar service, skipping it");
    public static string ServiceNotAvailableForUser(string service, string userId) => LC.L($"The service {service} is not available for user {userId}, skipping it");
    public static string LicenseWarning(GoogleRootType type, int approved) => LC.L($"Licensed Google Workspace feature seats exceeded for {type} ({approved}). Some items will not be backed up, and some folders may be empty.");
    public static string GoogleApiRetry(string path, int statusCode, int attempt, TimeSpan delay) => LC.L($"The Google API request {path} failed with status {statusCode}, retrying after {delay.TotalSeconds:0.#} seconds (attempt {attempt})");
    public static string GmailMessageGone(string messageId, string userId) => LC.L($"The message {messageId} of user {userId} was deleted after it was listed, skipping it");
    public static string GmailMessageFetchFailed(string messageId, string userId, int statusCode, string error) => LC.L($"Failed to read the message {messageId} of user {userId} (status {statusCode}): {error}");
    public static string GmailBatchRetry(int count, string userId, int statusCode, int attempt, TimeSpan delay) => LC.L($"{count} message(s) of user {userId} could not be read because of a rate limit (status {statusCode}), retrying after {delay.TotalSeconds:0.#} seconds (attempt {attempt})");
    public static string GmailHistoryExpired(string userId) => LC.L($"The stored Gmail history id of user {userId} is no longer accepted, listing the mailbox in full");
    public static string GmailHistoryIdMissing(string userId) => LC.L($"The Gmail history of user {userId} did not report a history id, so the mailbox is listed in full next time");
    public static string DriveChangesTokenRejected(string driveName, int statusCode) => LC.L($"The stored change token of drive {driveName} was rejected (status {statusCode}), listing the drive in full");
    public static string DriveStateUnreadable(string driveName) => LC.L($"The stored change tracking state of drive {driveName} cannot be read, listing the drive in full");
    public static string DriveChangesTooMany(string driveName, int limit) => LC.L($"Drive {driveName} reports more than {limit} changes, listing the drive in full instead");
    public static string DriveChangesArrangeFailed(string driveName) => LC.L($"The changes reported for drive {driveName} could not be arranged for listing, listing the drive in full");
    public static string DriveChangesTokenMissing(string driveName) => LC.L($"The changes feed of drive {driveName} did not end with a new start page token, so the drive is listed in full next time");
    public static string CalendarSyncTokenExpired(string calendarId, string userId) => LC.L($"The stored sync token of calendar {calendarId} of user {userId} is no longer accepted, listing the calendar in full");
    public static string CalendarSyncTokenMissing(string calendarId, string userId) => LC.L($"The listing of calendar {calendarId} of user {userId} did not end with a sync token, so the calendar is listed in full next time");
    public static string ContactsSyncTokenExpired(string userId) => LC.L($"The stored sync token of the contacts of user {userId} is no longer accepted, listing the contacts in full");
    public static string ContactsSyncTokenMissing(string userId) => LC.L($"The listing of the contacts of user {userId} did not end with a sync token, so the contacts are listed in full next time");
    public static string GmailBatchRetriesExhausted(int count, string userId, int statusCode, string error) => LC.L($"Giving up reading {count} message(s) of user {userId} after repeated rate limit errors (status {statusCode}): {error}");
}
