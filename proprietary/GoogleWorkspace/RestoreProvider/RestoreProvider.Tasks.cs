// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.Tasks.v1.Data;

namespace Duplicati.Proprietary.GoogleWorkspace;

partial class RestoreProvider
{
    private TaskRestoreHelper? _taskRestoreHelper = null;
    internal TaskRestoreHelper TaskRestore => _taskRestoreHelper ??= new TaskRestoreHelper(this);

    internal class TaskRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetUserId = null;
        private bool _hasLoadedTargetInfo = false;

        public async Task<string?> GetUserIdAndTaskListTarget(CancellationToken cancel)
        {
            if (_hasLoadedTargetInfo)
                return _targetUserId;

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            if (target.Type == SourceItemType.User)
            {
                _targetUserId = target.Metadata.GetValueOrDefault("gsuite:Id");
            }
            else if (target.Type == SourceItemType.UserTasks)
            {
                _targetUserId = target.Path.TrimStart(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Skip(1).FirstOrDefault();
            }
            else if (target.Type == SourceItemType.TaskList)
            {
                _targetUserId = target.Metadata.GetValueOrDefault("gsuite:UserId");
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreTasksInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring tasks.");
            }

            _hasLoadedTargetInfo = true;

            if (string.IsNullOrWhiteSpace(_targetUserId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreTasksMissingUserId", null, $"Missing target userId for restoring tasks.");
                return null;
            }

            return _targetUserId;
        }

        public async Task<string?> CreateTaskList(string userId, TaskList taskList, CancellationToken cancel)
        {
            var tasksService = Provider._apiHelper.GetTasksService(userId);

            // Check for duplicates by title
            if (!Provider._ignoreExisting)
            {
                var existingLists = await tasksService.Tasklists.List().ExecuteAsync(cancel);
                var duplicate = existingLists.Items?.FirstOrDefault(l =>
                    l.Title?.Equals(taskList.Title, StringComparison.OrdinalIgnoreCase) == true);

                if (duplicate != null)
                {
                    Log.WriteInformationMessage(LOGTAG, "CreateTaskListSkipDuplicate", $"Task list {taskList.Title} already exists, skipping.");
                    return duplicate.Id;
                }
            }

            // Clean up properties
            taskList.Id = null;
            taskList.ETag = null;
            taskList.SelfLink = null;
            taskList.Updated = null;

            var createdList = await tasksService.Tasklists.Insert(taskList).ExecuteAsync(cancel);
            return createdList.Id;
        }

        public async Task<string?> CreateTask(string userId, string taskListId, Google.Apis.Tasks.v1.Data.Task task, CancellationToken cancel)
        {
            var tasksService = Provider._apiHelper.GetTasksService(userId);

            // Check for duplicates by title if available
            if (!Provider._ignoreExisting && !string.IsNullOrWhiteSpace(task.Title))
            {
                var existingTasks = await tasksService.Tasks.List(taskListId).ExecuteAsync(cancel);
                var duplicate = existingTasks.Items?.FirstOrDefault(t =>
                    t.Title?.Equals(task.Title, StringComparison.OrdinalIgnoreCase) == true);

                if (duplicate != null)
                {
                    Log.WriteInformationMessage(LOGTAG, "CreateTaskSkipDuplicate", $"Task {task.Title} already exists in list, skipping.");
                    return duplicate.Id;
                }
            }

            // Clean up properties
            task.Id = null;
            task.ETag = null;
            task.SelfLink = null;
            task.Parent = null;
            task.Position = null;

            var createdTask = await tasksService.Tasks.Insert(task, taskListId).ExecuteAsync(cancel);
            return createdTask.Id;
        }

        public async Task<string> GetOrCreateRestoredTaskList(string userId, CancellationToken cancel)
        {
            const string RESTORED_TASKLIST_NAME = "Restored";

            var tasksService = Provider._apiHelper.GetTasksService(userId);

            // Check if task list already exists
            var existingLists = await tasksService.Tasklists.List().ExecuteAsync(cancel);
            var existingList = existingLists.Items?.FirstOrDefault(l =>
                l.Title?.Equals(RESTORED_TASKLIST_NAME, StringComparison.OrdinalIgnoreCase) == true);

            if (existingList != null)
                return existingList.Id;

            // Create new task list
            var newTaskList = new TaskList
            {
                Title = RESTORED_TASKLIST_NAME
            };

            var createdList = await tasksService.Tasklists.Insert(newTaskList).ExecuteAsync(cancel);
            return createdList.Id;
        }
    }

    private async System.Threading.Tasks.Task RestoreTaskLists(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var taskLists = GetMetadataByType(SourceItemType.TaskList);
        if (taskLists.Count == 0)
            return;

        var userId = await TaskRestore.GetUserIdAndTaskListTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        foreach (var taskList in taskLists)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = taskList.Key;
                var metadata = taskList.Value;

                // Create TaskList from metadata
                var taskListData = new TaskList
                {
                    Title = metadata.GetValueOrDefault("gsuite:Name") ?? "Unnamed Task List"
                };

                var newListId = await TaskRestore.CreateTaskList(userId, taskListData, cancel);
                if (newListId != null)
                {
                    _restoredTaskListMap[originalPath] = newListId;
                }

                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreTaskListsFailed", ex, $"Failed to restore task list {taskList.Key}");
            }
        }
    }

    private async System.Threading.Tasks.Task RestoreTasks(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var tasks = GetMetadataByType(SourceItemType.Task);
        if (tasks.Count == 0)
            return;

        var userId = await TaskRestore.GetUserIdAndTaskListTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        // Get or create the "Restored" task list for orphaned tasks
        string? restoredTaskListId = null;

        foreach (var task in tasks)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = task.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTasksMissingContent", null, $"Missing content for task {originalPath}, skipping.");
                    continue;
                }

                // Find parent task list - tasks are stored under their tasklist's path
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "");

                // Try to find the parent task list in our restored map
                string? listId = null;
                if (!string.IsNullOrEmpty(parentPath) && _restoredTaskListMap.TryGetValue(parentPath, out var mappedListId))
                {
                    listId = mappedListId;
                }

                // If no parent list found, use/create the "Restored" task list
                if (listId == null)
                {
                    if (restoredTaskListId == null)
                    {
                        restoredTaskListId = await TaskRestore.GetOrCreateRestoredTaskList(userId, cancel);
                    }
                    listId = restoredTaskListId;
                    Log.WriteInformationMessage(LOGTAG, "RestoreTasksToRestoredList", $"Task {originalPath} has no parent list, adding to 'Restored' task list.");
                }

                Google.Apis.Tasks.v1.Data.Task? taskData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    taskData = await JsonSerializer.DeserializeAsync<Google.Apis.Tasks.v1.Data.Task>(contentStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
                }

                if (taskData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTasksInvalidContent", null, $"Invalid content for task {originalPath}, skipping.");
                    continue;
                }

                await TaskRestore.CreateTask(userId, listId, taskData, cancel);

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreTasksFailed", ex, $"Failed to restore task {task.Key}");
            }
        }
    }
}
