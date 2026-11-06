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
}
