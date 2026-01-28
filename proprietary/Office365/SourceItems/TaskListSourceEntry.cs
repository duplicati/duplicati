// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class TaskListSourceEntry(SourceProvider provider, string path, GraphUser user, GraphTodoTaskList taskList)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, taskList.Id)), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var task in provider.TodoApi.ListTaskListTasksAsync(user.Id, taskList.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new TaskListTaskSourceEntry(provider, this.Path, user, taskList, task);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", taskList.Id },
                { "o365:Type", SourceItemType.TaskList.ToString() },
                { "o365:Name", taskList.DisplayName ?? "" },
                { "o365:DisplayName", taskList.DisplayName ?? "" },
                { "o365:WellknownListName", taskList.WellknownListName ?? "" },
                { "o365:IsOwner", taskList.IsOwner?.ToString() ?? "" },
                { "o365:IsShared", taskList.IsShared?.ToString() ?? "" }
            }
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
}
