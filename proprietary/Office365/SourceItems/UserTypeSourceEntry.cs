// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class UserTypeSourceEntry(SourceProvider provider, string path, GraphUser user, Office365UserType userType)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, userType.ToString().ToLowerInvariant())), user.CreatedDateTime.FromGraphDateTime(), null)
{
    private static readonly string LOGTAG = Log.LogTagFromType<UserTypeSourceEntry>();

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", user.Id },
                { "o365:Type", userType switch
                    {
                        Office365UserType.Profile => SourceItemType.UserProfile.ToString(),
                        Office365UserType.Mailbox => SourceItemType.UserMailbox.ToString(),
                        Office365UserType.Calendar => SourceItemType.UserCalendar.ToString(),
                        Office365UserType.Contacts => SourceItemType.UserContacts.ToString(),
                        Office365UserType.Tasks => SourceItemType.UserTasks.ToString(),
                        Office365UserType.Notes => SourceItemType.UserNotes.ToString(),
                        Office365UserType.Planner => SourceItemType.UserPlannerTasks.ToString(),
                        Office365UserType.Chats => SourceItemType.UserChats.ToString(),
                        _ => null
                    }
                },
                { "o365:Name", userType switch
                    {
                        Office365UserType.Profile => "Profile",
                        Office365UserType.Mailbox => "Mailbox",
                        Office365UserType.Calendar => "Calendar",
                        Office365UserType.Contacts => "Contacts",
                        Office365UserType.Tasks => "Tasks",
                        Office365UserType.Notes => "Notes",
                        Office365UserType.Planner => "Planner",
                        Office365UserType.Chats => "Chats",
                        _ => null
                    }
                }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var entries = userType switch
        {
            Office365UserType.Profile => ProfileEntries(cancellationToken),
            Office365UserType.Mailbox => MailboxEntries(cancellationToken),
            Office365UserType.Calendar => CalendarEntries(cancellationToken),
            Office365UserType.Contacts => ContactsEntries(cancellationToken),
            Office365UserType.Tasks => TasksEntries(cancellationToken),
            Office365UserType.Notes => NotesEntries(cancellationToken),
            Office365UserType.Planner => PlannerEntries(cancellationToken),
            Office365UserType.Chats => ChatsEntries(cancellationToken),

            _ => null
        };

        if (entries is null)
        {
            Log.WriteWarningMessage(LOGTAG, "UnknownUserType", null, $"Unknown user type '{userType}' for user '{user.Id}'");
            yield break;
        }

        await foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            // Probe if the entry exists, if it is a stream resource
            if (entry is StreamResourceEntryFunction sref)
                if (!await sref.ProbeIfExistsAsync(cancellationToken).ConfigureAwait(false))
                    continue;

            yield return entry;
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> ProfileEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var res = new StreamResourceEntryFunction[] {
            new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "user.json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => provider.UserProfileApi.GetUserObjectStreamAsync(user.Id, ct)),

            new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "photo.jpg"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => provider.UserProfileApi.GetUserPhotoStreamAsync(user.Id, ct)),

            new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "licenses.json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => provider.UserProfileApi.GetUserLicenseDetailsStreamAsync(user.Id, ct)),

            new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "manager.json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => provider.UserProfileApi.GetUserManagerStreamAsync(user.Id, ct)),

            new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "teams.json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => provider.UserProfileApi.GetUserTeamMembershipStreamAsync(user.Id, ct)),

        };

        foreach (var entry in res)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return entry;
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> MailboxEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rootFolder = await provider.UserEmailApi.GetMailRootFolderAsync(user.Id, cancellationToken).ConfigureAwait(false);

        // Don't introduce another level for the root folder, just enumerate its contents here
        var p = new UserMailboxFolderSourceEntry(provider, user, this.Path, rootFolder);
        await foreach (var entry in p.Enumerate(cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return entry;
        }

        // Add Mailbox Settings
        yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "settings.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: (ct) => provider.UserEmailApi.GetMailboxSettingsStreamAsync(user.Id, ct),
            minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Type", SourceItemType.UserMailboxSettings.ToString() },
                { "o365:Name", "Mailbox Settings" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value)));

        // Add Mailbox Rules
        yield return new UserMailboxRulesSourceEntry(provider, user, this.Path);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> ContactsEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var contact in provider.ContactsApi.ListAllContactsAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var isContactGroup = await provider.ContactsApi.IsContactGroupAsync(user.Id, contact.Id, cancellationToken).ConfigureAwait(false);

            var ms = new MemoryStream();
            System.Text.Json.JsonSerializer.Serialize(ms, contact);
            var jsonBytes = ms.ToArray();

            yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, contact.Id + ".json"),
                createdUtc: contact.CreatedDateTime.FromGraphDateTime(),
                lastModificationUtc: contact.LastModifiedDateTime.FromGraphDateTime(),
                size: jsonBytes.Length,
                streamFactory: (ct) => Task.FromResult<Stream>(new MemoryStream(jsonBytes)),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                {
                    { "o365:v", "1" },
                    { "o365:Id", contact.Id },
                    { "o365:Type", isContactGroup
                        ? SourceItemType.UserContactGroup.ToString()
                        : SourceItemType.UserContact.ToString() },
                    { "o365:Name", contact.DisplayName },
                }
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value)));

            // Contact Photo
            var photoEntry = new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, contact.Id + ".photo"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => provider.ContactsApi.GetContactPhotoStreamAsync(user.Id, contact.Id, null, ct));

            if (await photoEntry.ProbeIfExistsAsync(cancellationToken).ConfigureAwait(false))
                yield return photoEntry;

            if (isContactGroup)
            {
                // Members
                var members = await provider.ContactsApi.ListContactGroupMembersAsync(user.Id, contact.Id, cancellationToken)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (members.Count > 0)
                {
                    var memMs = new MemoryStream();
                    System.Text.Json.JsonSerializer.Serialize(memMs, members);
                    var memBytes = memMs.ToArray();

                    yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, contact.Id + ".members.json"),
                        createdUtc: DateTime.UnixEpoch,
                        lastModificationUtc: DateTime.UnixEpoch,
                        size: memBytes.Length,
                        streamFactory: (ct) => Task.FromResult<Stream>(new MemoryStream(memBytes)));
                }
            }
        }

        await foreach (var contactFolder in provider.ContactsApi.ListContactFoldersAsync(user.Id, null, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new UserContactFolderSourceEntry(provider, user, this.Path, contactFolder);
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> TasksEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {

        await foreach (var taskList in provider.TodoApi.ListUserTaskListsAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new TaskListSourceEntry(provider, this.Path, user, taskList);
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> NotesEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var note in provider.OnenoteApi.ListUserNotebooksAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new NotebookSourceEntry(provider, this.Path, note);
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> CalendarEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var calendar in provider.CalendarApi.ListUserCalendarGroupsAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new CalendarGroupSourceEntry(provider, this.Path, user, calendar);
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> PlannerEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var plan in provider.UserPlannerApi.ListUserAssignedPlannerTasksAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var ms = new MemoryStream();
            System.Text.Json.JsonSerializer.Serialize(ms, plan);
            ms.Seek(0, SeekOrigin.Begin);

            yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, plan.Id + ".json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => Task.FromResult<Stream>(ms),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                {
                    { "o365:v", "1" },
                    { "o365:Id", plan.Id },
                    { "o365:Type", SourceItemType.UserPlannerTasks.ToString() },
                    { "o365:Name", plan.Title },
                }
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value))
                );
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> ChatsEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chat in provider.ChatApi.ListUserChatsAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new ChatSourceEntry(provider, this.Path, user, chat);
        }
    }
}