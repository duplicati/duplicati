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
/// Shuts down or restarts the computer the server runs on
/// </summary>
public interface ISystemPowerControl
{
    /// <summary>
    /// Checks if this process is allowed to shut down or restart the computer, without performing the action.
    /// The answer can change while the server runs, such as when another user logs in.
    /// </summary>
    /// <param name="action">The action to check</param>
    /// <param name="cancellationToken">The token that cancels the check</param>
    /// <returns>True if the action is expected to work</returns>
    Task<bool> CanPerformAsync(QueueCompletionAction action, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the operating system to shut down or restart the computer.
    /// The request is handed to the operating system, which carries it out after a short delay where it supports one.
    /// </summary>
    /// <param name="action">The action to perform, which is not <see cref="QueueCompletionAction.None"/></param>
    /// <param name="cancellationToken">The token that cancels waiting for the operating system to accept the request</param>
    /// <returns>A task that completes when the operating system has accepted the request</returns>
    Task PerformAsync(QueueCompletionAction action, CancellationToken cancellationToken);
}
