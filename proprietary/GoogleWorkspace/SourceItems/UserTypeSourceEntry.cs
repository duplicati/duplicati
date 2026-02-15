// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class UserTypeSourceEntry(SourceProvider provider, string parentPath, string userId, string name, SourceItemType type)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, name)), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            yield break;

        var source = type switch
        {
            SourceItemType.UserGmail => EnumerateGmail(cancellationToken),
            SourceItemType.UserCalendar => EnumerateCalendar(cancellationToken),
            SourceItemType.UserContacts => EnumerateContacts(cancellationToken),
            SourceItemType.UserDrive => EnumerateDrive(cancellationToken),
            SourceItemType.UserTasks => EnumerateTasks(cancellationToken),
            SourceItemType.UserKeep => EnumerateKeep(cancellationToken),
            SourceItemType.UserChat => EnumerateChat(cancellationToken),
            _ => throw new NotImplementedException()
        };

        await foreach (var entry in source)
            yield return entry;
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateGmail([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetGmailService(userId);
        var request = service.Users.Labels.List(userId);
        Google.Apis.Gmail.v1.Data.ListLabelsResponse response;

        try
        {
            response = await request.ExecuteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Customize exception to indicate which user failed
            throw new Exception($"Error enumerating Gmail labels for user {userId}: {ex.Message}", ex);
        }

        if (response.Labels != null)
        {
            foreach (var label in response.Labels)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new GmailLabelSourceEntry(userId, this.Path, label, service);
            }
        }

        if (cancellationToken.IsCancellationRequested)
            yield break;

        yield return new GmailSettingsSourceEntry(userId, this.Path, service);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateCalendar([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetCalendarService(userId);
        var driveService = provider.ApiHelper.GetDriveService(userId);
        var aclService = provider.AvoidCalendarAcl ? null : provider.ApiHelper.GetCalendarAclService(userId);
        var request = service.CalendarList.List();

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var calendars = await request.ExecuteAsync(cancellationToken);

            if (calendars.Items != null)
            {
                foreach (var calendar in calendars.Items)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return new CalendarSourceEntry(this.Path, userId, calendar, service, driveService, aclService);
                }
            }
            nextPageToken = calendars.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateContacts([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var peopleService = provider.ApiHelper.GetPeopleService(userId);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new ContactsFolderSourceEntry(this.Path, peopleService);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new ContactGroupsFolderSourceEntry(this.Path, peopleService);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateDrive([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var driveService = provider.ApiHelper.GetDriveService(userId);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveFolderSourceEntry(this.Path, userId, "My Drive", "root", driveService);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new SharedDrivesSourceEntry(provider, this.Path, userId, driveService);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateTasks([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetTasksService(userId);
        var request = service.Tasklists.List();

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var taskLists = await request.ExecuteAsync(cancellationToken);

            if (taskLists.Items != null)
            {
                foreach (var taskList in taskLists.Items)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return new TaskListSourceEntry(this.Path, taskList, service);
                }
            }
            nextPageToken = taskLists.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateKeep([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;

        var service = provider.ApiHelper.GetKeepService(userId);
        var request = service.Notes.List();

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var notes = await request.ExecuteAsync(cancellationToken);

            if (notes.Notes != null)
            {
                foreach (var note in notes.Notes)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return new KeepNoteSourceEntry(this.Path, note, service);
                }
            }
            nextPageToken = notes.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));

    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateChat([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetChatService(userId);
        var driveService = provider.ApiHelper.GetDriveService(userId);

        foreach (var spaceType in new[] { "SPACE", "GROUP_CHAT", "DIRECT_MESSAGE" })
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return new ChatSpaceTypeSourceEntry(this.Path, spaceType, service, driveService);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "gsuite:v", "1" },
                { "gsuite:Type", type.ToString() },
                { "gsuite:Name", System.IO.Path.GetFileName(Path) },
                { "gsuite:Id", System.IO.Path.GetFileName(Path) }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
