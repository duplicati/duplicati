// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// Describes a Microsoft Graph application permission and what it is required for.
/// </summary>
/// <param name="Name">The name of the application permission.</param>
/// <param name="Description">A human readable description of the resources the permission affects.</param>
/// <param name="RequiredForBackup">Whether the permission is required for backup operations.</param>
/// <param name="RequiredForRestore">Whether the permission is required for restore operations.</param>
/// <param name="CoveredBy">The name of a write permission that includes this permission's access, if any. A granted covering permission makes this permission effectively enabled.</param>
internal sealed record RequiredGraphPermission(
    string Name,
    string Description,
    bool RequiredForBackup,
    bool RequiredForRestore,
    string? CoveredBy = null
);

/// <summary>
/// The Microsoft Graph application permissions used by the Office365 provider,
/// and helpers to inspect the permissions granted to the app registration.
/// </summary>
internal static class GraphPermissions
{
    /// <summary>
    /// The application permissions required for backup and restore operations.
    /// Read permissions with a write counterpart are only required for backup; restore
    /// operations are expected to hold the write permission, which includes read access.
    /// </summary>
    public static readonly IReadOnlyList<RequiredGraphPermission> Required =
    [
        new("User.Read.All", "Read user accounts, profiles, photos, and license assignments.", true, false, "User.ReadWrite.All"),
        new("User.ReadWrite.All", "Read and update user profiles, such as restoring the profile photo.", false, true),
        new("Group.Read.All", "Read groups, their members, owners, and conversations.", true, false, "Group.ReadWrite.All"),
        new("Group.ReadWrite.All", "Read and create groups, and manage members, owners, and conversations.", false, true),
        new("Mail.Read", "Read mailbox folders, messages, attachments, and inbox rules.", true, false, "Mail.ReadWrite"),
        new("Mail.ReadWrite", "Read, create, and update mailbox folders, messages, attachments, and inbox rules.", false, true),
        new("MailboxSettings.Read", "Read user mailbox settings.", true, false, "MailboxSettings.ReadWrite"),
        new("MailboxSettings.ReadWrite", "Read and update user mailbox settings.", false, true),
        new("Calendars.Read", "Read user and group calendars and events.", true, false, "Calendars.ReadWrite"),
        new("Calendars.ReadWrite", "Read, create, and update calendars, events, and event attachments.", false, true),
        new("Contacts.Read", "Read contact folders and contacts.", true, false, "Contacts.ReadWrite"),
        new("Contacts.ReadWrite", "Read, create, and update contact folders, contacts, and contact photos.", false, true),
        new("Files.Read.All", "Read OneDrive drives, files, and sharing permissions.", true, false, "Files.ReadWrite.All"),
        new("Files.ReadWrite.All", "Read, create, and update OneDrive files and sharing permissions.", false, true),
        new("Sites.Read.All", "Read SharePoint sites, lists, and list items.", true, false, "Sites.ReadWrite.All"),
        new("Sites.ReadWrite.All", "Read, create, and update SharePoint sites, lists, and list items.", false, true),
        new("Notes.Read.All", "Read OneNote notebooks, sections, and pages.", true, false, "Notes.ReadWrite.All"),
        new("Notes.ReadWrite.All", "Read, create, and update OneNote notebooks, sections, and pages.", false, true),
        new("Tasks.Read.All", "Read Planner plans, buckets, tasks, and To Do lists.", true, false, "Tasks.ReadWrite.All"),
        new("Tasks.ReadWrite.All", "Read, create, and update Planner plans, buckets, tasks, and To Do lists.", false, true),
        // Note: Team.ReadBasic.All, Channel.ReadBasic.All, TeamsTab.Read.All,
        // TeamsAppInstallation.ReadForTeam.All, TeamsTab.ReadWrite.All, and
        // TeamsAppInstallation.ReadWriteForTeam.All are the least-privileged permissions
        // for reading teams, channels, tabs, and installed apps, and for creating tabs
        // and installing apps, but Graph also accepts Group.Read.All / Group.ReadWrite.All
        // for those endpoints. Since the group permissions are already required, the
        // Teams-specific permissions are redundant and deliberately not listed.
        new("TeamMember.Read.All", "Read team memberships.", true, false),
        new("Channel.Create", "Create channels when restoring.", false, true),
        new("ChannelMessage.Read.All", "Read Teams channel messages and replies. Restore reads messages to detect duplicates.", true, true),
        new("Chat.Read.All", "Read Teams chat messages.", true, false),
        new("Teamwork.Migrate.All", "Restore Teams channels and channel messages in migration mode.", false, true),
    ];

    /// <summary>
    /// Extracts the granted application permissions from an access token acquired through
    /// the OAuth2 client credentials flow. The granted application permissions are listed
    /// in the token's <c>roles</c> claim, so no additional Graph API call is required.
    /// </summary>
    /// <param name="accessToken">The access token to inspect.</param>
    /// <returns>The names of the granted application permissions.</returns>
    public static IReadOnlySet<string> ExtractGrantedRoles(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
            throw new UserInformationException("The access token is not a valid JWT token.", "InvalidAccessToken");

        // The payload segment is base64url encoded
        var base64 = parts[1].Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(Convert.FromBase64String(base64));
        if (doc.RootElement.TryGetProperty("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in rolesElement.EnumerateArray())
            {
                if (role.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(role.GetString()))
                    roles.Add(role.GetString()!);
            }
        }

        return roles;
    }
}
