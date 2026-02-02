// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class TaskListTaskSubitemsSourceEntry(SourceProvider provider, string path, GraphUser user, GraphTodoTaskList taskList, GraphTodoTask task, TaskListTaskType type)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, type.ToString().ToLowerInvariant())), task.CreatedDateTime.FromGraphDateTime(), task.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (type == TaskListTaskType.ChecklistItems)
        {
            await foreach (var checklistItem in provider.TodoApi.ListTaskChecklistItemsAsync(user.Id, taskList.Id, task.Id, cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, checklistItem.Id + ".json"),
                    createdUtc: checklistItem.CreatedDateTime.FromGraphDateTime(),
                    lastModificationUtc: task.LastModifiedDateTime.FromGraphDateTime(),
                    size: -1,
                    streamFactory: (ct) => provider.TodoApi.GetTaskChecklistItemStreamAsync(user.Id, taskList.Id, task.Id, checklistItem.Id, ct),
                    minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                    {
                        { "o365:v", "1" },
                        { "o365:Id", checklistItem.Id },
                        { "o365:Type", SourceItemType.TaskListChecklistItem.ToString() },
                        { "o365:Name", checklistItem.DisplayName ?? "" },
                        { "o365:IsChecked", checklistItem.IsChecked?.ToString() ?? "" }
                    }
                    .Where(kv => !string.IsNullOrEmpty(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value)));
            }
            yield break;
        }

        if (type == TaskListTaskType.LinkedResources)
        {
            await foreach (var linkedResource in provider.TodoApi.ListTaskLinkedResourcesAsync(user.Id, taskList.Id, task.Id, cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, linkedResource.Id + ".json"),
                    createdUtc: task.CreatedDateTime.FromGraphDateTime(),
                    lastModificationUtc: task.LastModifiedDateTime.FromGraphDateTime(),
                    size: -1,
                    streamFactory: (ct) => provider.TodoApi.GetTaskLinkedResourceStreamAsync(user.Id, taskList.Id, task.Id, linkedResource.Id, ct),
                    minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                    {
                        { "o365:v", "1" },
                        { "o365:Id", linkedResource.Id },
                        { "o365:Type", SourceItemType.TaskListLinkedResource.ToString() },
                        { "o365:Name", linkedResource.DisplayName ?? "" },
                        { "o365:WebUrl", linkedResource.WebUrl ?? "" }
                    }
                    .Where(kv => !string.IsNullOrEmpty(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value)));
            }
            yield break;
        }

        throw new InvalidOperationException($"Unsupported TaskListTaskType: {type}");
    }
}

