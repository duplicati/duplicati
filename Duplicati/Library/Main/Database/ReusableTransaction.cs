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
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalDatabase));

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
    public void Commit(string? message = null, bool restart = true)
    {
        if (m_transaction == null)
            throw new InvalidOperationException("Transaction is already disposed");

        if (m_transaction != null)
        {
            using (var timer = string.IsNullOrWhiteSpace(message) ? null : new Logging.Timer(LOGTAG, message, "CommitTransaction"))
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
