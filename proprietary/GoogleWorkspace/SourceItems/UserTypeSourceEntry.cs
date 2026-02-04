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

                yield return new GmailLabelSourceEntry(provider, userId, this.Path, label);
            }
        }

        if (cancellationToken.IsCancellationRequested)
            yield break;

        yield return new GmailSettingsSourceEntry(provider, userId, this.Path);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateCalendar([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetCalendarService();
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
                    yield return new CalendarSourceEntry(provider, this.Path, calendar);
                }
            }
            nextPageToken = calendars.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateContacts([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new ContactsFolderSourceEntry(provider, this.Path);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new ContactGroupsFolderSourceEntry(provider, this.Path);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateDrive([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveSourceEntry(provider, userId, this.Path);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateTasks([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetTasksService();
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
                    yield return new TaskListSourceEntry(provider, this.Path, taskList);
                }
            }
            nextPageToken = taskLists.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateKeep([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new KeepSourceEntry(provider, this.Path);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateChat([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new ChatSourceEntry(provider, this.Path);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "gsuite:v", "1" },
                { "gsuite:Type", type.ToString() },
                { "gsuite:Name", System.IO.Path.GetFileName(Path) },
                { "gsuite:id", System.IO.Path.GetFileName(Path) }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
