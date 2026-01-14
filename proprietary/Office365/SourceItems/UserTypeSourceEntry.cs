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
    }

    private async IAsyncEnumerable<ISourceProviderEntry> ContactsEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var contact in provider.ContactsApi.ListAllContactsAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var ms = new MemoryStream();
            System.Text.Json.JsonSerializer.Serialize(ms, contact);
            ms.Seek(0, SeekOrigin.Begin);

            yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, contact.Id + ".json"),
                createdUtc: contact.CreatedDateTime.FromGraphDateTime(),
                lastModificationUtc: contact.LastModifiedDateTime.FromGraphDateTime(),
                size: -1,
                streamFactory: (ct) => Task.FromResult<Stream>(ms),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                {
                    { "o365:v", "1" },
                    { "o365:Id", contact.Id },
                    { "o365:Type", SourceItemType.UserContact.ToString() },
                    { "o365:Name", contact.DisplayName },
                }
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value)));
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

            yield return new NotebookSourceEntry(provider, this.Path, user, note);
        }
    }

    // private async IAsyncEnumerable<ISourceProviderEntry> OneDriveEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    // {
    //     await foreach (var drive in provider.OneDriveApi.ListUserDrivesAsync(user.Id, cancellationToken).ConfigureAwait(false))
    //     {
    //         if (cancellationToken.IsCancellationRequested)
    //             yield break;

    //         yield return new DriveSourceEntry(provider, this.Path, drive);
    //     }
    // }

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

            yield return new ChatSourceEntry(provider, this.Path, chat);
        }
    }
}