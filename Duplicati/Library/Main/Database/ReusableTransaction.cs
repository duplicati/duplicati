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
using System.Data;

#nullable enable

namespace Duplicati.Library.Main.Database;

/// <summary>
/// Wraps a transaction so it can be comitted and restarted
/// </summary>
internal class ReusableTransaction : IDisposable
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ReusableTransaction));

    /// <summary>
    /// The database to use
    /// </summary>
    private readonly LocalDatabase m_db;
    /// <summary>
    /// The current transaction
    /// </summary>
    private IDbTransaction? m_transaction;

    /// <summary>
    /// Creates a new reusable transaction
    /// </summary>
    /// <param name="db">The database to use</param>
    public ReusableTransaction(LocalDatabase db, IDbTransaction? transaction = null)
    {
        m_db = db;
        m_transaction = transaction ?? db.BeginTransaction();
    }

    /// <summary>
    /// The current transaction
    /// </summary>
    public IDbTransaction Transaction => m_transaction ?? throw new InvalidOperationException("Transaction is disposed");

    /// <summary>
    /// Commits the current transaction and optionally restarts it
    /// </summary>
    /// <param name="message">The log message to use</param>
    /// <param name="restart">True if the transaction should be restarted</param>
    public void Commit(string? message, bool restart = true)
    {
        if (m_transaction == null)
            throw new InvalidOperationException("Transaction is already disposed");

        if (m_transaction != null)
        {
            using (var timer = string.IsNullOrWhiteSpace(message) ? null : new Logging.Timer(LOGTAG, message, $"CommitTransaction: {message}"))
                m_transaction.Commit();
            m_transaction.Dispose();
            m_transaction = null;
        }

        if (restart)
            m_transaction = m_db.BeginTransaction();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        m_transaction?.Dispose();
        m_transaction = null;
    }
}
