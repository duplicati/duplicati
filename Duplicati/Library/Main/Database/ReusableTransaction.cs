// Copyright (C) 2025, The Duplicati Team
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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

#nullable enable

namespace Duplicati.Library.Main.Database;

/// <summary>
/// Wraps a transaction so it can be comitted and restarted.
/// </summary>
/// <remarks>
/// Creates a new reusable transaction.
/// </remarks>
/// <param name="db">The database to use.</param>
/// <param name="transaction">The transaction to use. If null, a new transaction is created.</param>
internal class ReusableTransaction(SqliteConnection con, SqliteTransaction? transaction = null) : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// The tag used for logging.
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ReusableTransaction));

    /// <summary>
    /// The database to use.
    /// </summary>
    private readonly SqliteConnection m_con = con;
    /// <summary>
    /// The current transaction.
    /// </summary>
    private SqliteTransaction m_transaction = transaction ?? con.BeginTransaction(deferred: true);
    /// <summary>
    /// True if the transaction is disposed.
    /// </summary>
    private bool m_disposed = false;

    /// <summary>
    /// Creates a new reusable transaction.
    /// </summary>
    /// <param name="db">The database this transaction relates to.</param>
    /// <param name="transaction">An optional existing transaction to use. If null, a new transaction is created.</param>
    public ReusableTransaction(LocalDatabase db, SqliteTransaction? transaction = null) : this(db.Connection, transaction) { }

    /// <summary>
    /// The current transaction.
    /// </summary>
    public SqliteTransaction Transaction => m_disposed ? throw new InvalidOperationException("Transaction is disposed") : m_transaction;

    /// <summary>
    /// Commits the transaction and restarts it.
    /// </summary>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the commit is done and a new transaction has been started.</returns>
    public async Task CommitAsync(CancellationToken token)
    {
        await CommitAsync(null, true, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Async version of Commit: <inheritdoc cref="Commit(string?, bool)"/>
    /// </summary>
    /// <param name="message">The log message to use.</param>
    /// <param name="restart">True if the transaction should be restarted.</param>
    /// <returns>A task that completes when the commit is done and (potentially) a new transaction has been started.</returns>
    /// <exception cref="InvalidOperationException">If the transaction is already Disposed.</exception>
    public async Task CommitAsync(string? message = null, bool restart = true, CancellationToken token = default)
    {
        if (m_disposed)
            throw new InvalidOperationException("Transaction is already disposed");

        message ??= "Unnamed commit";
        using (var timer = new Logging.Timer(LOGTAG, message, $"CommitTransaction: {message}"))
            await m_transaction.CommitAsync().ConfigureAwait(false);
        await m_transaction.DisposeAsync().ConfigureAwait(false);

        if (restart)
            m_transaction = m_con.BeginTransaction(deferred: true);
        else
            m_disposed = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        DisposeAsync().AsTask().Await();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!m_disposed)
        {
            try
            {
                using (var timer = new Logging.Timer(LOGTAG, "Dispose", "Rollback during transaction dispose"))
                    await m_transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "ReusableTransaction dispose", ex, "Transaction disposed with error: {0}", ex.Message);
                throw;
            }
            finally
            {
                m_disposed = true;
                await m_transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Rolls back the transaction and restarts it.
    /// </summary>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the rollback is done and a new transaction has been started.</returns>
    public async Task RollBackAsync(CancellationToken token)
    {
        await RollBackAsync(null, true, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Rolls back the transaction and optionally restarts it.
    /// </summary>
    /// <param name="message">Message to log.</param>
    /// <param name="restart">Whether to restart the transaction after rolling back.</param>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the rollback is done and (potentially) a new transaction has been started.</returns>
    /// <exception cref="InvalidOperationException">If the transaction has already been disposed.</exception>
    public async Task RollBackAsync(string? message = null, bool restart = true, CancellationToken token = default)
    {
        if (m_disposed)
            throw new InvalidOperationException("Transaction is already disposed");

        using (var timer = new Logging.Timer(LOGTAG, message, $"RollbackTransaction: {message}"))
            await m_transaction.RollbackAsync().ConfigureAwait(false);
        await m_transaction.DisposeAsync().ConfigureAwait(false);

        if (restart)
        {
            m_transaction = m_con.BeginTransaction();
        }
        else
        {
            m_disposed = true;
        }
    }
}
