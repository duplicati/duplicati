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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// The watchers, buffer and flush loop for one backup. A recording session is
/// not opened when the watchers start but by the first drain, so that it begins
/// at the moment the backup takes its snapshot; observations before that are
/// discarded, because the first backup scans everything anyway.
/// </summary>
public sealed class WatchedBackup : IAsyncDisposable
{
    private static readonly string LOGTAG = Log.LogTagFromType<WatchedBackup>();

    /// <summary>
    /// The number of consecutive failed writes after which the session is given up
    /// </summary>
    private const int MAX_CONSECUTIVE_FLUSH_FAILURES = 3;

    /// <summary>
    /// How long observations are kept when they cannot be used as a list: before the first
    /// session, and in a session that can no longer be trusted. Only the observations around
    /// the next backup's snapshot are needed then, and creating a snapshot takes far less
    /// </summary>
    internal static readonly TimeSpan UNUSABLE_RETENTION = TimeSpan.FromHours(1);

    private readonly ChangeTrackingDatabase _database;
    private readonly ChangeTrackingSettings _settings;
    private readonly FileSystemEventSourceFactory _factory;
    private readonly ChangeBuffer _buffer;
    private readonly List<IFileSystemEventSource> _sources = new();
    /// <summary>
    /// The identity of each watched folder when its watcher started
    /// </summary>
    private readonly List<(string Folder, string Identity)> _rootIdentities = new();
    /// <summary>
    /// Whether each root was a folder when its watcher started; a file root is watched through
    /// its parent without recursion, so a root that changes kind needs a new watcher
    /// </summary>
    private readonly List<(string Root, bool IsFolder)> _rootKinds = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _overflowLock = new();
    private TaskCompletionSource _flushSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _flushLoop;
    private int _consecutiveFlushFailures;
    private string? _overflowReason;
    private bool _overflowPersisted;
    /// <summary>
    /// Counts overflows, so a rotation can tell whether one arrived while it ran
    /// </summary>
    private long _overflowGeneration;
    /// <summary>
    /// When the recording was last found untrustworthy, in UTC ticks
    /// </summary>
    private long _lastOverflowTicks = long.MinValue;
    private volatile bool _healthy;
    /// <summary>
    /// Whether observations were dropped before the first session was opened
    /// </summary>
    private bool _droppedBeforeSession;

    /// <summary>
    /// The backup id
    /// </summary>
    public string BackupId { get; }

    /// <summary>
    /// The roots that are watched
    /// </summary>
    public IReadOnlyList<string> Roots { get; }

    /// <summary>
    /// The hash of the settings the current session records under
    /// </summary>
    public string SettingsHash { get; private set; }

    /// <summary>
    /// The current recording session, or null before the first drain
    /// </summary>
    public long? SessionId { get; private set; }

    /// <summary>
    /// When the current session was opened
    /// </summary>
    public DateTime? SessionStartedUtc { get; private set; }

    /// <summary>
    /// Whether every root is watched. A watcher that could not start, or that reported an
    /// error other than lost events, makes every session untrustworthy until it is restarted.
    /// </summary>
    public bool IsHealthy
    {
        get => _healthy;
        private set => _healthy = value;
    }

    /// <summary>
    /// Why the current session cannot be trusted, or null
    /// </summary>
    public string? OverflowReason
    {
        get
        {
            lock (_overflowLock)
                return _overflowReason;
        }
    }

    /// <summary>
    /// The number of observations not yet written to the database
    /// </summary>
    public int PendingChanges => _buffer.Count;

    /// <summary>
    /// Counts the times the recording was found untrustworthy; a change shows that events were lost
    /// </summary>
    public long OverflowGeneration
    {
        get
        {
            lock (_overflowLock)
                return _overflowGeneration;
        }
    }

    /// <summary>
    /// Creates a new watched backup, without starting it
    /// </summary>
    /// <param name="backupId">The backup id</param>
    /// <param name="roots">The source roots to watch</param>
    /// <param name="settingsHash">The hash of the backup settings</param>
    /// <param name="database">The tracking database</param>
    /// <param name="settings">The service settings</param>
    /// <param name="factory">The event source factory</param>
    public WatchedBackup(string backupId, IReadOnlyList<string> roots, string settingsHash, ChangeTrackingDatabase database, ChangeTrackingSettings settings, FileSystemEventSourceFactory factory)
    {
        BackupId = backupId;
        Roots = roots;
        SettingsHash = settingsHash;
        _database = database;
        _settings = settings;
        _factory = factory;
        _buffer = new ChangeBuffer(settings.HighWaterMark);
    }

    /// <summary>
    /// Starts the watchers and the flush loop. A root that cannot be watched is logged
    /// and makes the instance unhealthy; the other roots are still watched.
    /// </summary>
    public void Start()
    {
        if (_flushLoop != null)
            throw new InvalidOperationException("The watched backup is already started");

        var roots = Roots.ToList();
        try
        {
            roots.AddRange(DiscoverMountPoints(Roots));
        }
        catch (Exception ex)
        {
            IsHealthy = false;
            MarkOverflow($"MountPointDiscoveryFailed: {ex.Message}");
            Log.WriteWarningMessage(LOGTAG, "MountPointDiscoveryFailed", ex, "Could not list the volume mount points for backup {0}, so changes under a mounted volume might be missed", BackupId);
        }

        IsHealthy = _overflowReason == null;
        foreach (var root in roots)
        {
            var source = _factory(root);
            source.Changed += OnChanged;
            source.Error += OnError;
            try
            {
                var folder = RootIdentity.GetWatchedFolder(root);
                var identity = RootIdentity.Get(folder);
                var isFolder = Directory.Exists(root);
                source.Start();
                _sources.Add(source);
                if (identity != null)
                    _rootIdentities.Add((folder, identity));
                _rootKinds.Add((root, isFolder));
            }
            catch (Exception ex)
            {
                source.Dispose();
                IsHealthy = false;
                MarkOverflow($"CannotWatch {root}: {ex.GetType().Name}: {ex.Message}");
                Log.WriteWarningMessage(LOGTAG, "WatcherStartFailed", ex, "Could not watch {0} for backup {1}", root, BackupId);
            }
        }

        _flushLoop = Task.Run(FlushLoopAsync);
    }

    /// <summary>
    /// Checks that every watched folder is still the folder its watcher was started on. A folder
    /// replaced at the same path, as by a rename, is not watched, so the watcher is marked broken.
    /// </summary>
    /// <returns>True if every watched folder is unchanged</returns>
    public bool CheckRoots()
    {
        foreach (var (root, isFolder) in _rootKinds)
        {
            var nowFolder = Directory.Exists(root);
            var nowFile = !nowFolder && File.Exists(root);
            if ((isFolder && nowFile) || (!isFolder && nowFolder))
            {
                if (IsHealthy)
                {
                    IsHealthy = false;
                    Log.WriteWarningMessage(LOGTAG, "WatchedRootChangedKind", null, "The source {0} for backup {1} changed between a file and a folder, so the watcher will be restarted", root, BackupId);
                }
                MarkOverflow($"the source {root} changed between a file and a folder");
                return false;
            }
        }

        foreach (var (folder, identity) in _rootIdentities)
        {
            if (!string.Equals(RootIdentity.Get(folder), identity, StringComparison.Ordinal))
            {
                if (IsHealthy)
                {
                    IsHealthy = false;
                    Log.WriteWarningMessage(LOGTAG, "WatchedFolderReplaced", null, "The watched folder {0} for backup {1} was replaced or removed, so the watcher will be restarted", folder, BackupId);
                }
                MarkOverflow($"the watched folder {folder} was replaced or removed");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Finds the volumes mounted below the roots, which need watchers of their own
    /// </summary>
    /// <param name="roots">The roots</param>
    /// <returns>The mount paths below the roots, folders ending with a separator</returns>
    internal static IEnumerable<string> DiscoverMountPoints(IReadOnlyList<string> roots)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var comparison = Utility.Utility.ClientFilenameStringComparison;
        var folderRoots = roots.Select(Util.AppendDirSeparator).ToList();
        return WindowsVolumeMountPoints.GetMountPaths()
            .Where(mount => folderRoots.Any(root => mount.Length > root.Length && mount.StartsWith(root, comparison)))
            .Distinct(Utility.Utility.ClientFilenameStringComparer)
            .ToList();
    }

    private void OnChanged(string path, ChangeKind kind)
    {
        // A path that cannot be stored and listed as it is would be listed under another name
        if (!ChangeListWriter.IsRepresentable(path))
        {
            MarkOverflow("a changed path cannot be written to a change list");
            return;
        }

        // Before the first session the timer prunes the buffer; waking the loop on every
        // event above the mark would only repeat that work
        if (_buffer.Add(path, kind, DateTime.UtcNow.Ticks) && SessionId != null)
            _flushSignal.TrySetResult();
    }

    private void OnError(Exception ex)
    {
        // Lost events only affect the current recording. Any other error may have stopped the
        // watcher, or left part of the tree unwatched, so the watchers must be restarted
        if (ex is not InternalBufferOverflowException and not LostEventsException)
        {
            IsHealthy = false;
            Log.WriteWarningMessage(LOGTAG, "WatcherError", ex, "The watcher for backup {0} reported an error and will be restarted before the next backup", BackupId);
        }

        MarkOverflow($"{ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>
    /// Records that the current session can no longer be trusted
    /// </summary>
    /// <param name="reason">The reason</param>
    internal void MarkOverflow(string reason)
    {
        lock (_overflowLock)
        {
            _overflowGeneration++;
            _lastOverflowTicks = DateTime.UtcNow.Ticks;
            if (_overflowReason != null)
                return;

            _overflowReason = reason;
            _overflowPersisted = false;
        }

        Log.WriteWarningMessage(LOGTAG, "ChangeTrackingOverflow", null, "Change tracking for backup {0} can no longer vouch for its recording: {1}. The next backup scans all files.", BackupId, reason);
        _flushSignal.TrySetResult();
    }

    private async Task FlushLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var signal = _flushSignal.Task;
            // The delay is cancelled when the signal wins, so no timer is left behind
            using (var delayCancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                try
                {
                    await Task.WhenAny(Task.Delay(_settings.FlushInterval, delayCancel.Token), signal).ConfigureAwait(false);
                }
                finally
                {
                    delayCancel.Cancel();
                }
            }

            if (signal.IsCompleted)
                Interlocked.Exchange(ref _flushSignal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            if (_stop.IsCancellationRequested)
                break;

            try
            {
                await FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "FlushLoopError", ex, "Unexpected error while writing changes for backup {0}", BackupId);
            }
        }
    }

    /// <summary>
    /// Writes the buffered observations to the current session
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>False if the observations could not be written and are still buffered</returns>
    public async Task<bool> FlushAsync(CancellationToken cancellationToken)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await FlushUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private async Task<bool> FlushUnlockedAsync(CancellationToken cancellationToken)
    {
        // Before the first session is opened the observations stay in memory; the session
        // opened at the first backup's snapshot receives them, since a change observed just
        // before the snapshot may or may not be in it
        var sessionId = SessionId;
        if (sessionId == null)
        {
            // Only the observations around the first snapshot are needed, so old ones are
            // dropped; if that is not enough, the first session cannot be trusted
            if (_buffer.Count > _settings.HighWaterMark)
            {
                _buffer.PruneOlderThan(DateTime.UtcNow.Ticks - UNUSABLE_RETENTION.Ticks);
                if (_buffer.Count > _settings.MaxChanges)
                {
                    _buffer.Drain();
                    lock (_overflowLock)
                        _droppedBeforeSession = true;
                }
            }
            return true;
        }

        var changes = _buffer.Drain();

        try
        {
            if (changes.Count > 0)
            {
                var rows = await _database.WriteChangesAsync(sessionId.Value, changes, cancellationToken).ConfigureAwait(false);
                _consecutiveFlushFailures = 0;
                if (rows > _settings.MaxChanges)
                    MarkOverflow($"TooManyChanges: {rows} paths recorded, the limit is {_settings.MaxChanges}");
            }
        }
        catch (OperationCanceledException)
        {
            // Not a failure of the database; the changes are written by the next flush
            _buffer.Merge(changes);
            throw;
        }
        catch (Exception ex)
        {
            _buffer.Merge(changes);
            _consecutiveFlushFailures++;
            Log.WriteWarningMessage(LOGTAG, "FlushFailed", ex, "Could not write {0} changes for backup {1}, attempt {2}", changes.Count, BackupId, _consecutiveFlushFailures);
            if (_consecutiveFlushFailures >= MAX_CONSECUTIVE_FLUSH_FAILURES)
            {
                MarkOverflow($"FlushFailed: {ex.Message}");
                // The session is no longer used as a list, so only the observations around the
                // next snapshot are kept in memory while the database cannot be written
                _buffer.PruneOlderThan(DateTime.UtcNow.Ticks - UNUSABLE_RETENTION.Ticks);
            }
            return false;
        }

        string? reason;
        bool persisted;
        lock (_overflowLock)
        {
            reason = _overflowReason;
            persisted = _overflowPersisted;
        }

        if (reason != null && !persisted)
        {
            await _database.MarkOverflowedAsync(sessionId.Value, reason, cancellationToken).ConfigureAwait(false);
            lock (_overflowLock)
                _overflowPersisted = true;
        }

        // A session that cannot be trusted is never handed over as a list; only what is
        // observed around the next snapshot is still needed, so it does not grow without bound
        if (reason != null)
            await _database.DeleteChangesObservedBeforeAsync(sessionId.Value, DateTime.UtcNow.Ticks - UNUSABLE_RETENTION.Ticks, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Closes the current session, if any, and opens a new one. Every observation
    /// after the return belongs to the new session.
    /// </summary>
    /// <param name="settingsHash">The hash of the backup settings for the new session</param>
    /// <param name="taintSinceUtcTicks">
    /// The start of the window around the backup's snapshot. Events lost at or after it may
    /// be missing from the snapshot, so they make the new session untrustworthy as well.
    /// </param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>
    /// The id of the closed session, or null if none was open, why it cannot be trusted, or null,
    /// the token of the new session, and the overflow generation as of the rotation
    /// </returns>
    public async Task<(long? ClosedSessionId, string? ClosedOverflowReason, string NewToken, long OverflowGeneration)> RotateAsync(string settingsHash, long taintSinceUtcTicks, CancellationToken cancellationToken)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A stopped instance must not open sessions the service has already forgotten
            if (_stop.IsCancellationRequested)
                throw new ObjectDisposedException(nameof(WatchedBackup));

            // A replaced folder was not watched, which makes both sessions incomplete
            CheckRoots();
            var hadSession = SessionId != null;

            // What is buffered belongs to the session being closed. Observations that
            // arrive from here on stay in the buffer and are written to the new session
            var flushed = await FlushUnlockedAsync(cancellationToken).ConfigureAwait(false);

            string? closedReason;
            long generation;
            lock (_overflowLock)
            {
                closedReason = _overflowReason;
                generation = _overflowGeneration;
            }

            // Observations that could not be written are missing from the closed session;
            // they stay buffered and go to the new one
            if (!flushed)
                closedReason ??= "observations could not be written";

            var (closed, created, token) = await _database.RotateAsync(BackupId, settingsHash, cancellationToken).ConfigureAwait(false);
            SessionId = created;
            SessionStartedUtc = DateTime.UtcNow;
            SettingsHash = settingsHash;

            lock (_overflowLock)
            {
                if (_overflowGeneration != generation)
                {
                    // Events were lost while the sessions were switched, and they may belong
                    // to either, so neither can be trusted
                    closedReason ??= _overflowReason;
                }
                else if (_overflowReason != null && _lastOverflowTicks >= taintSinceUtcTicks)
                {
                    // Events were lost after the snapshot window opened, so they may be missing
                    // from the snapshot and must not be missing from the next list either
                }
                else
                {
                    // The new session is clean, unless the watchers themselves are broken
                    _overflowReason = IsHealthy ? null : closedReason;
                }

                // Observations dropped before the first session may include some around this snapshot
                if (!hadSession && _droppedBeforeSession)
                {
                    _overflowReason ??= "observations were dropped before the first backup";
                    _droppedBeforeSession = false;
                }

                _overflowPersisted = false;
                generation = _overflowGeneration;
            }

            return (closed, closedReason, token, generation);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// Stops the watchers and the flush loop, writing what is buffered
    /// </summary>
    public async Task StopAsync()
    {
        if (_stop.IsCancellationRequested)
            return;

        foreach (var source in _sources)
        {
            try
            {
                source.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "WatcherDisposeFailed", ex, "Could not stop a watcher for backup {0}", BackupId);
            }
        }
        _sources.Clear();

        _stop.Cancel();
        if (_flushLoop != null)
        {
            try
            {
                await _flushLoop.ConfigureAwait(false);
            }
            catch
            {
                // The loop logs its own errors
            }
        }

        try
        {
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "FinalFlushFailed", ex, "Could not write the last changes for backup {0}", BackupId);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stop.Dispose();
        _flushLock.Dispose();
    }
}
