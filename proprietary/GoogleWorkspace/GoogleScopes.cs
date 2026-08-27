// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Calendar.v3;
using Google.Apis.Drive.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Groupssettings.v1;
using Google.Apis.HangoutsChat.v1;
using Google.Apis.Keep.v1;
using Google.Apis.PeopleService.v1;
using Google.Apis.Tasks.v1;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Describes a Google OAuth scope and what it is required for.
/// </summary>
/// <param name="Name">The OAuth scope URL.</param>
/// <param name="Description">A human readable description of the resources the scope affects.</param>
/// <param name="RequiredForBackup">Whether the scope is required for backup operations.</param>
/// <param name="RequiredForRestore">Whether the scope is required for restore operations.</param>
/// <param name="CoveredBy">The URL of a write scope that includes this scope's access, if any. A granted covering scope makes this scope effectively enabled.</param>
public sealed record RequiredGoogleScope(
    string Name,
    string Description,
    bool RequiredForBackup,
    bool RequiredForRestore,
    string? CoveredBy = null
);

/// <summary>
/// The Google OAuth scopes used by the Google Workspace provider.
/// </summary>
public static class GoogleScopes
{
    /// <summary>
    /// The OAuth scopes required for backup and restore operations.
    /// Readonly scopes with a write counterpart are only required for backup; restore
    /// operations are expected to hold the write scope, which includes read access.
    /// </summary>
    public static readonly IReadOnlyList<RequiredGoogleScope> Required =
    [
        new(GmailService.Scope.GmailReadonly, "Read Gmail messages and labels.", true, false, GmailService.Scope.GmailModify),
        new(GmailService.Scope.GmailModify, "Read and restore Gmail messages and labels.", false, true),
        new(DirectoryService.Scope.AdminDirectoryUserReadonly, "Read user accounts.", true, false, DirectoryService.Scope.AdminDirectoryUser),
        new(DirectoryService.Scope.AdminDirectoryUser, "Read, create, and update user accounts.", false, true),
        new(DirectoryService.Scope.AdminDirectoryGroupReadonly, "Read groups and their members.", true, false, DirectoryService.Scope.AdminDirectoryGroup),
        new(DirectoryService.Scope.AdminDirectoryGroup, "Read, create, and update groups and their members.", false, true),
        new(DirectoryService.Scope.AdminDirectoryOrgunitReadonly, "Read organizational units.", true, false, DirectoryService.Scope.AdminDirectoryOrgunit),
        new(DirectoryService.Scope.AdminDirectoryOrgunit, "Read, create, and update organizational units.", false, true),
        new(CalendarService.Scope.CalendarReadonly, "Read calendars and events.", true, false, CalendarService.Scope.Calendar),
        new(CalendarService.Scope.Calendar, "Read calendar sharing settings (ACLs), and restore calendars and events.", true, true),
        new(DriveService.Scope.DriveReadonly, "Read Drive files and shared drives.", true, false, DriveService.Scope.Drive),
        new(DriveService.Scope.Drive, "Read and restore Drive files and shared drives.", false, true),
        new(PeopleServiceService.Scope.ContactsReadonly, "Read contacts.", true, false, PeopleServiceService.Scope.Contacts),
        new(PeopleServiceService.Scope.Contacts, "Read and restore contacts.", false, true),
        new(TasksService.Scope.TasksReadonly, "Read task lists and tasks.", true, false, TasksService.Scope.Tasks),
        new(TasksService.Scope.Tasks, "Read and restore task lists and tasks.", false, true),
        new(KeepService.Scope.KeepReadonly, "Read Keep notes.", true, false, KeepService.Scope.Keep),
        new(KeepService.Scope.Keep, "Read and restore Keep notes.", false, true),
        new(GroupssettingsService.Scope.AppsGroupsSettings, "Read and manage group settings.", true, true),
        new(HangoutsChatService.Scope.ChatSpacesReadonly, "Read Chat spaces.", true, false),
        new(HangoutsChatService.Scope.ChatMessagesReadonly, "Read Chat messages.", true, false),
        new(HangoutsChatService.Scope.ChatMembershipsReadonly, "Read Chat memberships.", true, false),
        new(HangoutsChatService.Scope.ChatMessages, "Restore Chat messages.", false, true),
        new(HangoutsChatService.Scope.ChatSpaces, "Create Chat spaces.", false, true),
    ];

    /// <summary>
    /// Other OAuth scopes from the same API families that are commonly granted, but never
    /// used by the provider. For service accounts there is no API to enumerate the scopes
    /// delegated in the Admin console, so these are probed individually to detect grants
    /// that are not needed. The flags are always <c>false</c>; they exist only to reuse
    /// the record shape.
    /// </summary>
    public static readonly IReadOnlyList<RequiredGoogleScope> KnownExtras =
    [
        new(GmailService.Scope.MailGoogleCom, "Full access to Gmail mailboxes.", false, false),
        new(GmailService.Scope.GmailSend, "Send Gmail messages.", false, false),
        new(GmailService.Scope.GmailCompose, "Create and send Gmail messages and drafts.", false, false),
        new(GmailService.Scope.GmailInsert, "Insert Gmail messages.", false, false),
        new(GmailService.Scope.GmailLabels, "Manage Gmail labels.", false, false),
        new(GmailService.Scope.GmailMetadata, "Read Gmail message metadata.", false, false),
        new(GmailService.Scope.GmailSettingsBasic, "Manage basic Gmail settings.", false, false),
        new(GmailService.Scope.GmailSettingsSharing, "Manage Gmail sharing settings.", false, false),
        new(DirectoryService.Scope.AdminDirectoryDomainReadonly, "Read directory domains.", false, false),
        new(DirectoryService.Scope.AdminDirectoryDomain, "Manage directory domains.", false, false),
        new(DirectoryService.Scope.AdminDirectoryUserschemaReadonly, "Read directory user schemas.", false, false),
        new(DirectoryService.Scope.AdminDirectoryUserschema, "Manage directory user schemas.", false, false),
        new(DirectoryService.Scope.AdminDirectoryRolemanagementReadonly, "Read directory role assignments.", false, false),
        new(DirectoryService.Scope.AdminDirectoryRolemanagement, "Manage directory role assignments.", false, false),
        new(CalendarService.Scope.CalendarEvents, "Manage calendar events.", false, false),
        new(CalendarService.Scope.CalendarEventsReadonly, "Read calendar events.", false, false),
        new(CalendarService.Scope.CalendarSettingsReadonly, "Read calendar settings.", false, false),
        new(DriveService.Scope.DriveFile, "Access Drive files created or opened by the app.", false, false),
        new(DriveService.Scope.DriveMetadata, "Manage Drive file metadata.", false, false),
        new(DriveService.Scope.DriveMetadataReadonly, "Read Drive file metadata.", false, false),
        new(PeopleServiceService.Scope.ContactsOtherReadonly, "Read other contacts.", false, false),
        new(PeopleServiceService.Scope.DirectoryReadonly, "Read directory profile data.", false, false),
        new(HangoutsChatService.Scope.ChatDelete, "Delete Chat messages and spaces.", false, false),
        new(HangoutsChatService.Scope.ChatImport, "Import Chat spaces and messages.", false, false),
        new(HangoutsChatService.Scope.ChatMemberships, "Manage Chat memberships.", false, false),
        new(HangoutsChatService.Scope.ChatMessagesCreate, "Create Chat messages.", false, false),
        new(HangoutsChatService.Scope.ChatSpacesCreate, "Create Chat spaces.", false, false),
    ];
}
