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

using Duplicati.Server.Serialization;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Keeps the action to perform on the computer once the task queue has run empty,
/// such as shutting it down or running the user's script after a set of manually started backups.
/// The action is kept in memory only, and is performed at most once.
/// </summary>
public interface IQueueCompletionActionService
{
    /// <summary>
    /// The action that is performed when the task queue has run empty
    /// </summary>
    QueueCompletionAction PendingAction { get; }

    /// <summary>
    /// Gets the actions this server is allowed to perform on the computer it runs on,
    /// which does not include <see cref="QueueCompletionAction.None"/>
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the check</param>
    /// <returns>The supported actions, which is empty when the computer cannot be shut down by the server</returns>
    Task<IReadOnlyList<QueueCompletionAction>> GetSupportedActionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the action to perform when the task queue has run empty, replacing the current one.
    /// Setting <see cref="QueueCompletionAction.None"/> cancels a pending action.
    /// </summary>
    /// <param name="action">The action to perform</param>
    /// <param name="cancellationToken">The token that cancels the request</param>
    /// <exception cref="Exceptions.ConflictException">Thrown when an action is requested while nothing is running or queued</exception>
    /// <exception cref="Exceptions.BadRequestException">Thrown when the server is not allowed to perform the action, or no script is set</exception>
    Task SetPendingActionAsync(QueueCompletionAction action, CancellationToken cancellationToken);
}
