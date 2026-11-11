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


using Duplicati.Library.AutoUpdater;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Drives the automatic installation of a detected update
/// </summary>
public interface IUpdateInstallService
{
    /// <summary>
    /// How the running server is hosted
    /// </summary>
    HostEnvironment Host { get; }

    /// <summary>
    /// Whether updates can be installed automatically on this installation
    /// </summary>
    UpdateInstallSupport Support { get; }

    /// <summary>
    /// True while an install is in progress
    /// </summary>
    bool InProgress { get; }

    /// <summary>
    /// Starts installing the update recorded by the poll thread.
    /// Throws a conflict exception if no update is available, a task is running, or an install is already in progress.
    /// </summary>
    void BeginInstall();

    /// <summary>
    /// Cancels an install that has not yet handed control to the helper
    /// </summary>
    void CancelInstall();

    /// <summary>
    /// Completes an install started by a previous instance, if one is pending; runs in the background
    /// </summary>
    void CompletePendingInstall();

    /// <summary>
    /// Checks the configured update channel and installs the newest update, as asked by the console.
    /// The install starts right away when no task is running, and otherwise as soon as the queue is idle.
    /// Throws if the update cannot be installed, for example when the same version failed to install recently.
    /// </summary>
    /// <param name="cancellationToken">Cancels the update check</param>
    /// <returns>What the service did with the request</returns>
    Task<ConsoleUpdateInstallResult> InstallLatestForConsoleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What the update service did with an install request from the console
/// </summary>
public enum ConsoleUpdateInstallStatus
{
    /// <summary>
    /// The running version is the newest on the update channel
    /// </summary>
    UpToDate,
    /// <summary>
    /// The update is being downloaded and installed
    /// </summary>
    Installing,
    /// <summary>
    /// The update is installed once the running and queued tasks have finished
    /// </summary>
    WaitingForIdle
}

/// <summary>
/// The outcome of an install request from the console
/// </summary>
/// <param name="Status">What the service did</param>
/// <param name="Version">The version being installed, or the running version when it is up to date</param>
public sealed record ConsoleUpdateInstallResult(ConsoleUpdateInstallStatus Status, string Version);
