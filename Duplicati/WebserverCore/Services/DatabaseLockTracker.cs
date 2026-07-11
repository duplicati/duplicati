// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or 
// sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System.Collections.Concurrent;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Tracks which local backup database files are currently in use by running operations,
/// preventing concurrent access that would cause SQLite "database is locked" errors.
/// </summary>
/// <remarks>
/// Each database path gets its own <see cref="SemaphoreSlim"/> (initial count 1).
/// Queued tasks call <see cref="AcquireAsync"/> which waits for the lock.
/// Immediate tasks call <see cref="TryAcquire"/> which fails instantly if locked.
/// <para>
/// Semaphore entries are intentionally not removed on release. Removing them is racy: a
/// concurrent acquirer could take the lock between a release and a dictionary removal,
/// after which a new acquirer would create a fresh semaphore for the same path, breaking
/// mutual exclusion. The dictionary is bounded by the number of unique database paths
/// (one per backup), which is a small, long-lived set, so the lack of cleanup does not
/// cause unbounded growth in practice.
/// </para>
/// </remarks>
public class DatabaseLockTracker : IDatabaseLockTracker
{
    private const int MaxLockCount = 1;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(Library.Utility.Utility.ClientFilenameStringComparer);

    /// <inheritdoc />
    public async Task<IAsyncDisposable> AcquireAsync(string dbPath, CancellationToken cancellationToken)
    {
        var semaphore = GetOrCreateSemaphore(dbPath);
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new LockRelease(semaphore);
    }

    /// <inheritdoc />
    public IAsyncDisposable? TryAcquire(string dbPath)
    {
        var semaphore = GetOrCreateSemaphore(dbPath);
        if (!semaphore.Wait(0))
            return null;
        return new LockRelease(semaphore);
    }

    /// <summary>
    /// Normalizes the database path and gets (or creates) the semaphore for it.
    /// </summary>
    private SemaphoreSlim GetOrCreateSemaphore(string dbPath)
    {
        var normalized = NormalizePath(dbPath);
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("Database path cannot be null or empty", nameof(dbPath));
        return _locks.GetOrAdd(normalized, _ => new SemaphoreSlim(MaxLockCount, MaxLockCount));
    }

    /// <summary>
    /// Normalizes a file path so that the same database file always maps to the same key,
    /// regardless of path separators or relative vs. absolute forms.
    /// </summary>
    private static string NormalizePath(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            return string.Empty;
        return Path.GetFullPath(dbPath);
    }

    /// <summary>
    /// A disposable handle that releases the semaphore when disposed.
    /// </summary>
    private sealed class LockRelease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
