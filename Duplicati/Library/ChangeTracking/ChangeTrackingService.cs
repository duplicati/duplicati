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
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// The change tracking service. There is one per server process; it owns the
/// tracking database, which it recreates on construction, and one
/// <see cref="WatchedBackup"/> per backup that has used the watcher since the
/// server started.
/// </summary>
public sealed class ChangeTrackingService : IChangeTrackingService
{
    private static readonly string LOGTAG = Log.LogTagFromType<ChangeTrackingService>();

    private readonly ChangeTrackingSettings _settings;
    private readonly FileSystemEventSourceFactory _factory;
    private readonly ChangeTrackingDatabase? _database;
    private readonly Dictionary<string, WatchedBackup> _watched = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    /// <inheritdoc />
    public bool Enabled => _settings.Enabled && _database != null;

    /// <summary>
    /// The settings in use
    /// </summary>
    public ChangeTrackingSettings Settings => _settings;

    private ChangeTrackingService(ChangeTrackingSettings settings, FileSystemEventSourceFactory factory, ChangeTrackingDatabase? database)
    {
        _settings = settings;
        _factory = factory;
        _database = database;
    }

    /// <summary>
    /// Creates the service and its database. An existing database is deleted, because
    /// the server cannot know what happened while it was not running.
    /// </summary>
    /// <param name="settings">The settings</param>
    /// <param name="factory">The event source factory; the operating system watcher in production</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The service</returns>
    public static async Task<ChangeTrackingService> CreateAsync(ChangeTrackingSettings settings, FileSystemEventSourceFactory factory, CancellationToken cancellationToken)
    {
        try
        {
            var database = await ChangeTrackingDatabase.CreateAsync(settings.DatabasePath, cancellationToken).ConfigureAwait(false);
            return new ChangeTrackingService(settings, factory, database);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The watcher is an optimization; without its database every backup scans all files
            Log.WriteWarningMessage(LOGTAG, "ChangeTrackingDatabaseFailed", ex, "The change tracking database {0} could not be created, so the file watcher is disabled", settings.DatabasePath);
            return new ChangeTrackingService(settings, factory, null);
        }
    }

    /// <summary>
    /// The database; only used while the service is enabled
    /// </summary>
    private ChangeTrackingDatabase Database => _database ?? throw new InvalidOperationException("The change tracking database is not available");

    /// <inheritdoc />
    public async Task<Handoff> PrepareHandoffAsync(string backupId, IReadOnlyList<string> sources, string settingsHash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupId);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsHash);

        if (!Enabled)
            return new Handoff(this, backupId, settingsHash, false, "change tracking is disabled");

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (!_watched.TryGetValue(backupId, out var watched))
            {
                _watched[backupId] = StartWatching(backupId, sources, settingsHash);
                Log.WriteInformationMessage(LOGTAG, "ChangeTrackingStarted", "Started watching {0} source paths for backup {1}; this run scans all files", sources.Count, backupId);
                return new Handoff(this, backupId, settingsHash, false, "first run since the watcher started");
            }

            if (!watched.CheckRoots() || !watched.IsHealthy || !watched.Roots.SequenceEqual(sources, Utility.Utility.ClientFilenameStringComparer))
            {
                var reason = watched.IsHealthy ? "the sources changed" : (watched.OverflowReason ?? "the watcher is not healthy");
                // Forgotten first, so a failure below cannot leave a stopped watcher registered
                _watched.Remove(backupId);
                await watched.DisposeAsync().ConfigureAwait(false);
                await Database.DeleteSessionsForBackupAsync(backupId, CancellationToken.None).ConfigureAwait(false);
                _watched[backupId] = StartWatching(backupId, sources, settingsHash);
                Log.WriteInformationMessage(LOGTAG, "ChangeTrackingRestarted", "Restarted watching for backup {0} because {1}; this run scans all files", backupId, reason);
                return new Handoff(this, backupId, settingsHash, false, reason);
            }

            if (watched.SessionId == null)
                return new Handoff(this, backupId, settingsHash, false, "no recording session is open");

            if (!string.Equals(watched.SettingsHash, settingsHash, StringComparison.Ordinal))
                return new Handoff(this, backupId, settingsHash, false, "the backup settings changed");

            var overflow = watched.OverflowReason;
            if (overflow != null)
                return new Handoff(this, backupId, settingsHash, false, overflow);

            return new Handoff(this, backupId, settingsHash, true, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    private WatchedBackup StartWatching(string backupId, IReadOnlyList<string> sources, string settingsHash)
    {
        var watched = new WatchedBackup(backupId, sources.ToList(), settingsHash, Database, _settings, _factory);
        watched.Start();
        return watched;
    }

    /// <summary>
    /// Closes the recording session and writes the list file. Called by the backup
    /// through the handoff once the snapshot exists.
    /// </summary>
    /// <param name="handoff">The handoff</param>
    /// <param name="referenceTimeUtc">The time the backup took before creating its snapshot</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The handoff for the backup, or null if the backup is not watched</returns>
    internal async Task<ChangeListHandoff?> DrainAsync(Handoff handoff, DateTime referenceTimeUtc, CancellationToken cancellationToken)
    {
        // The completion waits for a drain that is still running, as when the backup gave up on it
        await handoff.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (handoff.Completed || handoff.Rotated)
                return null;

            return await DrainUnlockedAsync(handoff, referenceTimeUtc, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            handoff.Gate.Release();
        }
    }

    private async Task<ChangeListHandoff?> DrainUnlockedAsync(Handoff handoff, DateTime referenceTimeUtc, CancellationToken cancellationToken)
    {
        var watched = await GetWatchedAsync(handoff.BackupId, cancellationToken).ConfigureAwait(false);
        if (watched == null)
        {
            Log.WriteInformationMessage(LOGTAG, "ChangeTrackingNotActive", "Backup {0} is not being watched; this run scans all files", handoff.BackupId);
            return null;
        }

        // The session is always rotated, so the next run starts from this run's snapshot
        var taintSince = referenceTimeUtc.Ticks - _settings.GraceDelay.Ticks;
        var (closedId, closedOverflow, newToken, generation) = await watched.RotateAsync(handoff.SettingsHash, taintSince, cancellationToken).ConfigureAwait(false);
        handoff.Rotated = true;
        handoff.ClosedSessionId = closedId;
        var newId = watched.SessionId ?? throw new InvalidOperationException("No session is open after the rotation");

        var listPath = Path.Combine(_settings.TempFolder, $"duplicati-changes-{Guid.NewGuid():N}.txt");
        try
        {
            // Late events may still arrive for the closed session; they land in the new
            // one, which is read below as well
            await Task.Delay(_settings.GraceDelay, cancellationToken).ConfigureAwait(false);
            var flushed = await watched.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Lost events are reported late, so events from before the rotation may be among them
            if (watched.OverflowGeneration != generation)
                closedOverflow ??= "events were lost around the snapshot";
            // Late events that could not be written would be missing from the list
            if (!flushed)
                closedOverflow ??= "observations could not be written";

            if (closedId == null)
            {
                Log.WriteInformationMessage(LOGTAG, "ChangeTrackingSessionOpened", "Opened the first recording session for backup {0}; this run scans all files", handoff.BackupId);
                return new ChangeListHandoff(null, newToken, []);
            }

            // Everything observed since just before the snapshot may or may not be in it,
            // so it is reported to this run and to the next
            var straddled = await Database.CopyObservedSinceAsync(closedId.Value, newId, taintSince, cancellationToken).ConfigureAwait(false);

            if (!handoff.Usable || closedOverflow != null)
            {
                await Database.DeleteSessionAsync(closedId.Value, CancellationToken.None).ConfigureAwait(false);
                handoff.ClosedSessionId = null;
                Log.WriteInformationMessage(LOGTAG, "ChangeTrackingFullScan", "Backup {0} scans all files because {1}", handoff.BackupId, closedOverflow ?? handoff.FullScanReason);
                return new ChangeListHandoff(null, newToken, []);
            }

            var baseTokens = await Database.GetBaseTokensAsync(closedId.Value, cancellationToken).ConfigureAwait(false);
            var count = await ChangeListWriter.WriteAsync(listPath, Database.ReadChangesAsync(closedId.Value, cancellationToken), Database.ReadChangesAsync(newId, cancellationToken), cancellationToken).ConfigureAwait(false);

            // The backup may have been edited while the list was read, which deletes its sessions;
            // a list read from a deleted session would be incomplete
            if (await GetWatchedAsync(handoff.BackupId, cancellationToken).ConfigureAwait(false) != watched
                || await Database.GetSessionAsync(closedId.Value, cancellationToken).ConfigureAwait(false) == null
                || await Database.GetSessionAsync(newId, cancellationToken).ConfigureAwait(false) == null)
            {
                TryDelete(listPath);
                handoff.ClosedSessionId = null;
                Log.WriteInformationMessage(LOGTAG, "ChangeTrackingReset", "Change tracking for backup {0} was reset while its list was read; this run scans all files", handoff.BackupId);
                return new ChangeListHandoff(null, newToken, []);
            }

            handoff.Drained = true;
            handoff.ListPath = listPath;
            handoff.EntryCount = count;

            Log.WriteInformationMessage(LOGTAG, "ChangeTrackingHandoff", "Handing backup {0} a change list with {1} entries, {2} of them observed around the snapshot", handoff.BackupId, count, straddled);
            return new ChangeListHandoff(listPath, newToken, baseTokens);
        }
        catch (Exception ex)
        {
            // Nothing is lost: the closed session is folded into the new one, which then also
            // accepts the closed session's base. The backup scans everything without a token,
            // so the next run scans everything too, unless this run is retried against the base
            TryDelete(listPath);
            handoff.Drained = false;
            handoff.ListPath = null;
            if (closedId != null && handoff.ClosedSessionId != null)
            {
                // A session that could not be trusted, or holds a path the list cannot carry,
                // must not lend its base or its paths to the new one; the backup scans everything
                if (ex is not ChangeListPathException && handoff.Usable && closedOverflow == null)
                    await Database.MergeSessionIntoAsync(closedId.Value, newId, CancellationToken.None).ConfigureAwait(false);
                else
                    await Database.DeleteSessionAsync(closedId.Value, CancellationToken.None).ConfigureAwait(false);
            }
            handoff.ClosedSessionId = null;

            if (ex is ChangeListPathException)
            {
                // The list cannot carry the path, so this run scans everything with the new token
                Log.WriteWarningMessage(LOGTAG, "ChangeListPathNotRepresentable", ex, "A changed path cannot be written to the change list, so backup {0} scans all files", handoff.BackupId);
                return new ChangeListHandoff(null, newToken, []);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task CompleteHandoffAsync(Handoff handoff, bool success, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        // A drain the backup gave up on may still be running; wait for it to settle
        await handoff.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            handoff.Completed = true;
            if (!handoff.Drained || handoff.ClosedSessionId == null)
                return;

            var closedId = handoff.ClosedSessionId.Value;
            var watched = await GetWatchedAsync(handoff.BackupId, cancellationToken).ConfigureAwait(false);
            var currentId = watched?.SessionId;

            if (success || currentId == null)
            {
                await Database.DeleteSessionAsync(closedId, cancellationToken).ConfigureAwait(false);
                if (!success)
                    Log.WriteInformationMessage(LOGTAG, "ChangeTrackingSessionDropped", "The run for backup {0} did not complete and the watcher was reset, so the next run scans all files", handoff.BackupId);
            }
            else
            {
                // The run did not produce a complete fileset, so the next run rescans these paths
                // too; the merged session accepts the older fileset as its base as well
                await Database.MergeSessionIntoAsync(closedId, currentId.Value, cancellationToken).ConfigureAwait(false);
                Log.WriteInformationMessage(LOGTAG, "ChangeTrackingSessionMerged", "The run for backup {0} did not complete, so its {1} changed paths are kept for the next run", handoff.BackupId, handoff.EntryCount);
            }
        }
        finally
        {
            handoff.Gate.Release();
            handoff.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<bool> StopAsync(string backupId, CancellationToken cancellationToken)
    {
        WatchedBackup? watched;
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return false;

            if (_watched.Remove(backupId, out watched))
                await watched.DisposeAsync().ConfigureAwait(false);

            // After the watcher is stopped, which waits for a rotation in progress and refuses later
            // ones, so a session such a rotation opened is deleted too
            if (_database != null)
                await _database.DeleteSessionsForBackupAsync(backupId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        if (watched != null)
            Log.WriteInformationMessage(LOGTAG, "ChangeTrackingStopped", "Stopped watching backup {0}; the next run scans all files", backupId);
        return watched != null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChangeTrackingStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        List<WatchedBackup> watched;
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            watched = _watched.Values.ToList();
        }
        finally
        {
            _lock.Release();
        }

        var result = new List<ChangeTrackingStatus>();
        foreach (var w in watched)
        {
            var recorded = w.SessionId == null ? 0 : await Database.CountChangesAsync(w.SessionId.Value, cancellationToken).ConfigureAwait(false);
            result.Add(new ChangeTrackingStatus(w.BackupId, w.IsHealthy, w.SessionStartedUtc, w.PendingChanges, recorded, w.OverflowReason));
        }

        return result;
    }

    private async Task<WatchedBackup?> GetWatchedAsync(string backupId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _watched.GetValueOrDefault(backupId);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ChangeTrackingService));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The file is a leftover at worst
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        List<WatchedBackup> watched;
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            watched = _watched.Values.ToList();
            _watched.Clear();
        }
        finally
        {
            _lock.Release();
        }

        foreach (var w in watched)
        {
            try
            {
                await w.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "WatchedBackupDisposeFailed", ex, "Could not stop watching backup {0}", w.BackupId);
            }
        }

        if (_database != null)
            await _database.DisposeAsync().ConfigureAwait(false);
    }
}
