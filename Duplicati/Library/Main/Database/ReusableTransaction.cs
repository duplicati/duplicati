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
internal class ReusableTransaction(LocalDatabase db, SqliteTransaction? transaction = null) : IDisposable
{
    /// <summary>
    /// The tag used for logging.
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ReusableTransaction));

    /// <summary>
    /// The database to use.
    /// </summary>
    private readonly LocalDatabase m_db = db;
    /// <summary>
    /// The current transaction.
    /// </summary>
    private SqliteTransaction m_transaction = transaction ?? db.Connection.BeginTransaction();
    /// <summary>
    /// True if the transaction is disposed.
    /// </summary>
    private bool m_disposed = false;

    /// <summary>
    /// The current transaction.
    /// </summary>
    public SqliteTransaction Transaction => m_disposed ? m_transaction : throw new InvalidOperationException("Transaction is disposed");

    /// <summary>
    /// Commits the current transaction and optionally restarts it.
    /// </summary>
    /// <remarks>
    /// Calls the Async version of this method and awaits it.
    /// </remarks>
    /// <param name="message">The log message to use.</param>
    /// <param name="restart">True if the transaction should be restarted.</param>
    /// <exception cref="InvalidOperationException">If the transaction is already Disposed.</exception>
    public void Commit(string? message, bool restart = true)
    {
        CommitAsync(message, restart).Await();
    }

    /// <summary>
    /// Async version of Commit: <inheritdoc cref="Commit(string?, bool)"/>
    /// </summary>
    /// <param name="message">The log message to use.</param>
    /// <param name="restart">True if the transaction should be restarted.</param>
    /// <returns>An awaitable task.</returns>
    /// <exception cref="InvalidOperationException">If the transaction is already Disposed.</exception>
    public async Task CommitAsync(string? message, bool restart = true)
    {
        if (m_disposed)
            throw new InvalidOperationException("Transaction is already disposed");

        using (var timer = string.IsNullOrWhiteSpace(message) ? null : new Logging.Timer(LOGTAG, message, $"CommitTransaction: {message}"))
            await m_transaction.CommitAsync();
        await m_transaction.DisposeAsync();

        if (restart)
            m_transaction = m_db.Connection.BeginTransaction();
        else
            m_disposed = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Calls the Async version of this method and awaits it.
    /// </remarks>
    public void Dispose()
    {
        DisposeAsync().Await();
    }

    /// <summary>
    /// Async version of Dispose: <inheritdoc cref="Dispose()"/>
    /// </summary>
    /// <returns>An awaitable task.</returns>
    public async Task DisposeAsync()
    {
        if (!m_disposed)
        {
            m_disposed = true;
            await m_transaction.DisposeAsync();
        }
    }
}
