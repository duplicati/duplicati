// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Globalization;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal enum TaskListTaskType
{
    ChecklistItems,
    LinkedResources
}

internal class TaskListTaskSourceEntry(SourceProvider provider, string path, GraphUser user, GraphTodoTaskList taskList, GraphTodoTask task)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, task.Id)), task.CreatedDateTime.FromGraphDateTime(), task.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            yield break;

        yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "content.json"),
            createdUtc: task.CreatedDateTime.FromGraphDateTime(),
            lastModificationUtc: task.LastModifiedDateTime.FromGraphDateTime(),
            size: -1,
            streamFactory: (ct) => provider.TodoApi.GetTaskStreamAsync(user.Id, taskList.Id, task.Id, ct),
            minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", task.Id },
                { "o365:Type", SourceItemType.TaskListTask.ToString() },
                { "o365:Name", task.Title ?? "" },
                { "o365:LastModifiedDateTime", task.LastModifiedDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) },
                { "o365:CreatedDateTime", task.CreatedDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) },
                { "o365:Status", task.Status ?? "" },
                { "o365:Importance", task.Importance ?? "" }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value)));

        yield return new TaskListTaskSubitemsSourceEntry(provider, this.Path, user, taskList, task, TaskListTaskType.ChecklistItems);
        yield return new TaskListTaskSubitemsSourceEntry(provider, this.Path, user, taskList, task, TaskListTaskType.LinkedResources);

    }
}
