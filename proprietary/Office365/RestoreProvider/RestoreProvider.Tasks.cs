// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

public partial class RestoreProvider
{
    internal TodoApiImpl TodoApi => new TodoApiImpl(_apiHelper);

    internal class TodoApiImpl(APIHelper provider)
    {
        internal Task<GraphTodoTaskList> CreateTaskListAsync(string userIdOrUpn, string displayName, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var url = $"{baseUrl}/v1.0/users/{user}/todo/lists";

            var content = new StringContent(JsonSerializer.Serialize(new { displayName }), Encoding.UTF8, "application/json");
            return provider.PostGraphItemAsync<GraphTodoTaskList>(url, content, ct);
        }

        internal Task<GraphTodoTask> CreateTaskAsync(string userIdOrUpn, string taskListId, GraphTodoTask task, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var list = Uri.EscapeDataString(taskListId);
            var url = $"{baseUrl}/v1.0/users/{user}/todo/lists/{list}/tasks";

            // Ensure ID is null to avoid errors
            task.Id = "";

            var content = new StringContent(JsonSerializer.Serialize(task), Encoding.UTF8, "application/json");
            return provider.PostGraphItemAsync<GraphTodoTask>(url, content, ct);
        }

        internal Task<GraphTodoChecklistItem> CreateChecklistItemAsync(string userIdOrUpn, string taskListId, string taskId, string displayName, bool isChecked, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var list = Uri.EscapeDataString(taskListId);
            var task = Uri.EscapeDataString(taskId);
            var url = $"{baseUrl}/v1.0/users/{user}/todo/lists/{list}/tasks/{task}/checklistItems";

            var content = new StringContent(JsonSerializer.Serialize(new { displayName, isChecked }), Encoding.UTF8, "application/json");
            return provider.PostGraphItemAsync<GraphTodoChecklistItem>(url, content, ct);
        }

        internal Task<GraphTodoLinkedResource> CreateLinkedResourceAsync(string userIdOrUpn, string taskListId, string taskId, GraphTodoLinkedResource resource, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var list = Uri.EscapeDataString(taskListId);
            var task = Uri.EscapeDataString(taskId);
            var url = $"{baseUrl}/v1.0/users/{user}/todo/lists/{list}/tasks/{task}/linkedResources";

            // Ensure ID is null
            resource.Id = "";

            var content = new StringContent(JsonSerializer.Serialize(resource), Encoding.UTF8, "application/json");
            return provider.PostGraphItemAsync<GraphTodoLinkedResource>(url, content, ct);
        }
    }

    private async Task RestoreTaskLists(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var taskLists = GetMetadataByType(SourceItemType.TaskList);
        if (taskLists.Count == 0)
            return;

        string? targetUserId = null;
        if (RestoreTarget.Type == SourceItemType.User)
        {
            targetUserId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreTaskListsMissingUser", null, "Could not determine target user for task list restore.");
            return;
        }

        foreach (var taskList in taskLists)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = taskList.Key;
                var metadata = taskList.Value;
                var displayName = metadata.GetValueOrDefault("o365:Name") ?? metadata.GetValueOrDefault("o365:DisplayName") ?? "Restored Task List";

                var newList = await TodoApi.CreateTaskListAsync(targetUserId, displayName, cancel);
                _restoredTaskListMap[originalPath] = newList.Id;
                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreTaskListsFailed", ex, $"Failed to restore task list {taskList.Key}");
            }
        }
    }

    private async Task RestoreTaskListTasks(CancellationToken cancel)
    {
        var tasks = GetMetadataByType(SourceItemType.TaskListTask);
        if (tasks.Count == 0)
            return;

        string? targetUserId = null;
        if (RestoreTarget?.Type == SourceItemType.User)
        {
            targetUserId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreTaskListTasksMissingUser", null, "Could not determine target user for task restore.");
            return;
        }

        foreach (var task in tasks)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = task.Key;

                // Find parent list
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));
                if (parentPath == null || !_restoredTaskListMap.TryGetValue(parentPath, out var listId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskListTasksMissingParent", null, $"Could not find parent list for task {originalPath}, skipping.");
                    continue;
                }

                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);
                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskListTasksMissingContent", null, $"Missing content for task {originalPath}, skipping.");
                    continue;
                }

                GraphTodoTask? taskObj;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    taskObj = await JsonSerializer.DeserializeAsync<GraphTodoTask>(contentStream, cancellationToken: cancel);
                }

                if (taskObj == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskListTasksInvalidContent", null, $"Invalid content for task {originalPath}, skipping.");
                    continue;
                }

                var newTask = await TodoApi.CreateTaskAsync(targetUserId, listId, taskObj, cancel);
                _restoredTaskMap[originalPath] = newTask.Id;

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var file);
                file?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreTaskListTasksFailed", ex, $"Failed to restore task {task.Key}");
            }
        }
    }

    private async Task RestoreTaskChecklistItems(CancellationToken cancel)
    {
        var items = GetMetadataByType(SourceItemType.TaskListChecklistItem);
        if (items.Count == 0)
            return;

        string? targetUserId = null;
        if (RestoreTarget?.Type == SourceItemType.User)
        {
            targetUserId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetUserId))
            return;

        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = item.Key;
                var metadata = item.Value;
                var displayName = metadata.GetValueOrDefault("o365:Name") ?? "Checklist Item";
                var isChecked = metadata.GetValueOrDefault("o365:IsChecked") == "True";

                // Find parent task
                // Path: .../Lists/ListId/Tasks/TaskId/ChecklistItems/ItemId
                // Parent: .../Lists/ListId/Tasks/TaskId/ChecklistItems
                // GrandParent: .../Lists/ListId/Tasks/TaskId

                var parentDir = Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)); // ChecklistItems
                if (parentDir == null) continue;

                var taskPath = Util.AppendDirSeparator(Path.GetDirectoryName(parentDir)); // TaskId

                if (taskPath == null || !_restoredTaskMap.TryGetValue(taskPath, out var taskId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskChecklistItemsMissingParent", null, $"Could not find parent task for checklist item {originalPath}, skipping.");
                    continue;
                }

                // We also need listId.
                // Task path: .../Lists/ListId/Tasks/TaskId
                // List path: .../Lists/ListId
                var tasksDir = Path.GetDirectoryName(taskPath.TrimEnd(Path.DirectorySeparatorChar)); // Tasks
                if (tasksDir == null) continue;

                var listPath = Util.AppendDirSeparator(Path.GetDirectoryName(tasksDir)); // ListId

                if (listPath == null || !_restoredTaskListMap.TryGetValue(listPath, out var listId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskChecklistItemsMissingList", null, $"Could not find parent list for checklist item {originalPath}, skipping.");
                    continue;
                }

                await TodoApi.CreateChecklistItemAsync(targetUserId, listId, taskId, displayName, isChecked, cancel);
                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreTaskChecklistItemsFailed", ex, $"Failed to restore checklist item {item.Key}");
            }
        }
    }

    private async Task RestoreTaskLinkedResources(CancellationToken cancel)
    {
        var resources = GetMetadataByType(SourceItemType.TaskListLinkedResource);
        if (resources.Count == 0)
            return;

        string? targetUserId = null;
        if (RestoreTarget?.Type == SourceItemType.User)
        {
            targetUserId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetUserId))
            return;

        foreach (var resource in resources)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = resource.Key;

                // Find parent task
                var parentDir = Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)); // LinkedResources
                if (parentDir == null) continue;

                var taskPath = Util.AppendDirSeparator(Path.GetDirectoryName(parentDir)); // TaskId

                if (taskPath == null || !_restoredTaskMap.TryGetValue(taskPath, out var taskId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskLinkedResourcesMissingParent", null, $"Could not find parent task for linked resource {originalPath}, skipping.");
                    continue;
                }

                // We also need listId.
                var tasksDir = Path.GetDirectoryName(taskPath.TrimEnd(Path.DirectorySeparatorChar)); // Tasks
                if (tasksDir == null) continue;

                var listPath = Util.AppendDirSeparator(Path.GetDirectoryName(tasksDir)); // ListId

                if (listPath == null || !_restoredTaskListMap.TryGetValue(listPath, out var listId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskLinkedResourcesMissingList", null, $"Could not find parent list for linked resource {originalPath}, skipping.");
                    continue;
                }

                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);
                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskLinkedResourcesMissingContent", null, $"Missing content for linked resource {originalPath}, skipping.");
                    continue;
                }

                GraphTodoLinkedResource? resourceObj;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    resourceObj = await JsonSerializer.DeserializeAsync<GraphTodoLinkedResource>(contentStream, cancellationToken: cancel);
                }

                if (resourceObj == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskLinkedResourcesInvalidContent", null, $"Invalid content for linked resource {originalPath}, skipping.");
                    continue;
                }

                await TodoApi.CreateLinkedResourceAsync(targetUserId, listId, taskId, resourceObj, cancel);

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var file);
                file?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreTaskLinkedResourcesFailed", ex, $"Failed to restore linked resource {resource.Key}");
            }
        }
    }
}
