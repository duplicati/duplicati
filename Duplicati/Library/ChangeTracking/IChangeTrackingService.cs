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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// The state of change tracking for one backup
/// </summary>
/// <param name="BackupId">The backup id</param>
/// <param name="Healthy">Whether every root is watched</param>
/// <param name="SessionStartedUtc">When the current session was opened, or null before the first drain</param>
/// <param name="PendingChanges">The number of observations not yet written</param>
/// <param name="RecordedChanges">The number of paths in the current session</param>
/// <param name="OverflowReason">Why the current session cannot be trusted, or null</param>
public sealed record ChangeTrackingStatus(string BackupId, bool Healthy, DateTime? SessionStartedUtc, long PendingChanges, long RecordedChanges, string? OverflowReason);

/// <summary>
/// Keeps file system watchers on the sources of backups between runs and hands
/// each run the paths that changed since the previous one
/// </summary>
public interface IChangeTrackingService : IAsyncDisposable
{
    /// <summary>
    /// Whether backups may use the watcher at all
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Prepares the handoff for a backup run: starts the watchers for the backup if
    /// they are not running, and decides whether the current session can be used.
    /// The session is not closed here but by the backup's drain call.
    /// </summary>
    /// <param name="backupId">The backup id</param>
    /// <param name="sources">The expanded local source roots of the backup</param>
    /// <param name="settingsHash">The hash of everything that decides what the backup scans</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The handoff, to be given to the controller as the change list provider</returns>
    Task<Handoff> PrepareHandoffAsync(string backupId, IReadOnlyList<string> sources, string settingsHash, CancellationToken cancellationToken);

    /// <summary>
    /// Finishes a handoff after the run. A successful run drops the closed session; any
    /// other outcome merges it into the current session so the next run rescans it.
    /// </summary>
    /// <param name="handoff">The handoff</param>
    /// <param name="success">Whether the run produced a complete fileset</param>
    /// <param name="cancellationToken">The cancellation token</param>
    Task CompleteHandoffAsync(Handoff handoff, bool success, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the watchers of a backup and forgets its sessions, as when the backup
    /// was edited or deleted, or no longer uses the watcher. A later run starts over.
    /// </summary>
    /// <param name="backupId">The backup id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>True if the backup was being watched</returns>
    Task<bool> StopAsync(string backupId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the state of every watched backup
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The states</returns>
    Task<IReadOnlyList<ChangeTrackingStatus>> GetStatusAsync(CancellationToken cancellationToken);
}
