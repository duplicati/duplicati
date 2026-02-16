// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Tasks.v1.Data;
using Google.Apis.Tasks.v1;
using System.Runtime.CompilerServices;
using Task = System.Threading.Tasks.Task;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class TaskListSourceEntry(string parentPath, TaskList taskList, TasksService tasksService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, taskList.Id)), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = tasksService.Tasks.List(taskList.Id);

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var tasks = await request.ExecuteAsync(cancellationToken);

            if (tasks.Items != null)
            {
                foreach (var task in tasks.Items)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return new TaskSourceEntry(this.Path, task);
                }
            }
            nextPageToken = tasks.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.TaskList.ToString() },
            { "gsuite:Name", taskList.Title },
            { "gsuite:Id", taskList.Id },
            { "gsuite:Updated", taskList.Updated },
            { "gsuite:ETag", taskList.ETag },
            { "gsuite:Kind", taskList.Kind },
            { "gsuite:SelfLink", taskList.SelfLink }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
