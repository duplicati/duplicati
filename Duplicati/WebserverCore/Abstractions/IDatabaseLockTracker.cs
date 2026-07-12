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

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Tracks which local backup database files are currently in use by running operations,
/// preventing concurrent access that would cause SQLite "database is locked" errors.
/// </summary>
public interface IDatabaseLockTracker
{
    /// <summary>
    /// Attempts to acquire a lock on the given database path, waiting until the lock
    /// is available. Used by queued tasks (backup, restore, etc.) which should wait
    /// for any concurrent immediate operations to finish.
    /// </summary>
    /// <param name="dbPath">The normalized, full path to the database file.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the wait.</param>
    /// <returns>A disposable handle that releases the lock when disposed.</returns>
    Task<IAsyncDisposable> AcquireAsync(string dbPath, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to acquire a lock on the given database path, failing immediately if
    /// the database is already in use. Used by immediate (non-queued) operations such
    /// as listing filesets from the restore UI.
    /// </summary>
    /// <param name="dbPath">The normalized, full path to the database file.</param>
    /// <returns>A disposable handle that releases the lock when disposed, or <c>null</c> if the database is already locked.</returns>
    IAsyncDisposable? TryAcquire(string dbPath);
}
