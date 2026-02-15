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
                _targetUserId = target.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
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
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskListsMissingContent", null, $"Missing content for task list {originalPath}, skipping.");
                    continue;
                }

                TaskList? taskListData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    taskListData = await JsonSerializer.DeserializeAsync<TaskList>(contentStream, cancellationToken: cancel);
                }

                if (taskListData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTaskListsInvalidContent", null, $"Invalid content for task list {originalPath}, skipping.");
                    continue;
                }

                var newListId = await TaskRestore.CreateTaskList(userId, taskListData, cancel);
                if (newListId != null)
                {
                    _restoredTaskListMap[originalPath] = newListId;
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
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

                // Find parent task list
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "");
                if (parentPath == null || !_restoredTaskListMap.TryGetValue(parentPath, out var listId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreTasksMissingParent", null, $"Could not find parent list for task {originalPath}, skipping.");
                    continue;
                }

                Google.Apis.Tasks.v1.Data.Task? taskData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    taskData = await JsonSerializer.DeserializeAsync<Google.Apis.Tasks.v1.Data.Task>(contentStream, cancellationToken: cancel);
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
