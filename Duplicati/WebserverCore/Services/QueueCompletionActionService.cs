// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Shuts down or restarts the computer, or runs the user's script, once the task queue has run empty.
/// The action is requested for the tasks that are queued at that time, so it is kept in memory only:
/// it is dropped when the server restarts, and it is performed at most once.
/// </summary>
public class QueueCompletionActionService : IQueueCompletionActionService
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<QueueCompletionActionService>();

    /// <summary>
    /// The message for a request that arrives while nothing is running or queued
    /// </summary>
    private const string NoTasksMessage = "There are no running or queued tasks to wait for";

    private readonly object _lock = new();
    private readonly Connection _connection;
    private readonly EventPollNotify _eventPollNotify;
    private readonly IQueueRunnerService _queueRunnerService;
    private readonly ISystemPowerControl _systemPowerControl;
    private readonly IPostQueueScriptRunner _postQueueScriptRunner;
    private readonly TimeSpan _settleDelay;
    private QueueCompletionAction _pendingAction = QueueCompletionAction.None;

    /// <summary>
    /// Creates the service and starts following the task queue
    /// </summary>
    /// <param name="connection">The server database connection, used to report a failed action</param>
    /// <param name="eventPollNotify">The source of the task queue events</param>
    /// <param name="queueRunnerService">The task queue to follow</param>
    /// <param name="systemPowerControl">The handler that shuts down or restarts the computer</param>
    /// <param name="postQueueScriptRunner">The handler that runs the user's script</param>
    /// <param name="settings">The timings, of which the settle delay is used</param>
    public QueueCompletionActionService(Connection connection, EventPollNotify eventPollNotify, IQueueRunnerService queueRunnerService, ISystemPowerControl systemPowerControl, IPostQueueScriptRunner postQueueScriptRunner, QueueCompletionSettings settings)
    {
        _connection = connection;
        _eventPollNotify = eventPollNotify;
        _queueRunnerService = queueRunnerService;
        _systemPowerControl = systemPowerControl;
        _postQueueScriptRunner = postQueueScriptRunner;
        _settleDelay = settings.SettleDelay;

        // A queued task can also leave the queue without running, so both events are followed
        eventPollNotify.TaskCompleted += (_, _) => ScheduleCheck();
        eventPollNotify.TaskQueueUpdate += (_, _) => ScheduleCheck();
    }

    /// <inheritdoc/>
    public QueueCompletionAction PendingAction
    {
        get
        {
            lock (_lock)
                return _pendingAction;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueueCompletionAction>> GetSupportedActionsAsync(CancellationToken cancellationToken)
    {
        var supported = new List<QueueCompletionAction>();
        foreach (var action in new[] { QueueCompletionAction.Shutdown, QueueCompletionAction.Reboot, QueueCompletionAction.RunScript })
            if (await CanPerformAsync(action, cancellationToken).ConfigureAwait(false))
                supported.Add(action);

        return supported;
    }

    /// <summary>
    /// Checks if an action can be performed on this computer
    /// </summary>
    /// <param name="action">The action to check</param>
    /// <param name="cancellationToken">The token that cancels the check</param>
    /// <returns>True if the action is expected to work</returns>
    private Task<bool> CanPerformAsync(QueueCompletionAction action, CancellationToken cancellationToken)
        => action == QueueCompletionAction.RunScript
            ? Task.FromResult(_postQueueScriptRunner.CanRun())
            : _systemPowerControl.CanPerformAsync(action, cancellationToken);

    /// <summary>
    /// Describes an action for the log and the notifications, as the words that follow "to" and "Failed to"
    /// </summary>
    /// <param name="action">The action to describe</param>
    /// <returns>The description</returns>
    private static string Describe(QueueCompletionAction action)
        => action switch
        {
            QueueCompletionAction.Reboot => "restart the computer",
            QueueCompletionAction.RunScript => "run the post-queue script",
            _ => "shut down the computer"
        };

    /// <inheritdoc/>
    public async Task SetPendingActionAsync(QueueCompletionAction action, CancellationToken cancellationToken)
    {
        // The queue is checked first, as that answer does not depend on the computer.
        // It is checked again below, as the queue can run empty while the permission is looked up.
        if (action != QueueCompletionAction.None)
        {
            if (IsQueueEmpty())
                throw new ConflictException(NoTasksMessage);
            if (!await CanPerformAsync(action, cancellationToken).ConfigureAwait(false))
                throw new BadRequestException(action == QueueCompletionAction.RunScript
                    ? $"No script is set in the server setting {Server.Database.ServerSettings.CONST.POST_QUEUE_PROCESSING_SCRIPT}, or the file does not exist"
                    : $"The server is not allowed to perform the action on this computer: {action}");
        }

        lock (_lock)
        {
            // Without a task there is no completion to wait for, and the action would wait for
            // the next task to finish, which could be a scheduled backup days later
            if (action != QueueCompletionAction.None && IsQueueEmpty())
                throw new ConflictException(NoTasksMessage);

            if (_pendingAction == action)
                return;

            _pendingAction = action;
        }

        Library.Logging.Log.WriteInformationMessage(LOGTAG, "QueueCompletionActionSet", "The action for when the task queue completes is now: {0}", action);
        _eventPollNotify.SignalNewEvent();
    }

    /// <summary>
    /// Checks if nothing is running or waiting in the task queue
    /// </summary>
    /// <returns>True if the queue is empty</returns>
    private bool IsQueueEmpty()
        // The waiting tasks are read first: a task leaves that list at the same time it becomes the running one
        => _queueRunnerService.GetCurrentTasks().Count == 0 && _queueRunnerService.GetCurrentTask() == null;

    /// <summary>
    /// Checks the queue after the settle delay, if an action is pending
    /// </summary>
    private void ScheduleCheck()
    {
        if (PendingAction == QueueCompletionAction.None)
            return;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_settleDelay).ConfigureAwait(false);
                await PerformIfQueueEmptyAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Library.Logging.Log.WriteErrorMessage(LOGTAG, "QueueCompletionActionCheckFailed", ex, "Failed to check the task queue for the completion action: {0}", ex.Message);
            }
        });
    }

    /// <summary>
    /// Performs the pending action if the queue is empty, and clears it so it only runs once
    /// </summary>
    private async Task PerformIfQueueEmptyAsync()
    {
        QueueCompletionAction action;
        lock (_lock)
        {
            if (_pendingAction == QueueCompletionAction.None || !IsQueueEmpty())
                return;

            action = _pendingAction;
            _pendingAction = QueueCompletionAction.None;
        }

        _eventPollNotify.SignalNewEvent();

        var description = Describe(action);
        try
        {
            Library.Logging.Log.WriteInformationMessage(LOGTAG, "QueueCompletionActionStarting", "The task queue has completed, about to {0}", description);
            if (action == QueueCompletionAction.RunScript)
                await _postQueueScriptRunner.RunAsync(CancellationToken.None).ConfigureAwait(false);
            else
                await _systemPowerControl.PerformAsync(action, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _connection.LogError(null, $"Failed to {description} after the task queue completed", ex);
            _connection.RegisterNotification(
                NotificationType.Error,
                $"Failed to {description}",
                $"The task queue completed, but it was not possible to {description}: {ex.Message}",
                ex,
                null,
                "",
                null,
                "QueueCompletionActionFailed",
                LOGTAG,
                (n, a) => n);
        }
    }
}
