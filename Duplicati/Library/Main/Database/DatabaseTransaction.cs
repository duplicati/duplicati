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

#nullable enable
using System;
using System.Data;

namespace Duplicati.Library.Main.Database;

public partial class DatabaseConnectionManager
{
    /// <summary>
    /// Helper class to manage a database transaction
    /// </summary>
    public class DatabaseTransaction : IDisposable
    {
        /// <summary>
        /// The transaction state
        /// </summary>
        public enum TransactionState
        {
            /// <summary>
            /// The transaction is open
            /// </summary>
            Open,
            /// <summary>
            /// The transaction has been committed
            /// </summary>
            Committed,
            /// <summary>
            /// The transaction has been rolled back
            /// </summary>
            RolledBack,
            /// <summary>
            /// The transaction has been disposed
            /// </summary>
            Disposed
        }

        /// <summary>
        /// The connection manager
        /// </summary>
        private readonly DatabaseConnectionManager m_manager;

        /// <summary>
        /// The transaction object
        /// </summary>
        private IDbTransaction? m_transaction;

        /// <summary>
        /// Gets the transaction object
        /// </summary>
        public IDbTransaction? Transaction
        {
            get => m_transaction;
            set => m_transaction = value;
        }

        /// <summary>
        /// Gets the transaction state
        /// </summary>
        public TransactionState State => m_state;

        /// <summary>
        /// The transaction state
        /// </summary>
        private TransactionState m_state;

        /// <summary>
        /// Creates a new transaction
        /// </summary>
        /// <param name="manager">The connection manager</param>
        /// <param name="transaction">The transaction object</param>
        public DatabaseTransaction(DatabaseConnectionManager manager, IDbTransaction? transaction)
        {
            m_manager = manager;
            m_transaction = transaction;
            m_state = TransactionState.Open;
        }

        /// <summary>
        /// Commits the transaction
        /// </summary>
        /// <param name="message">The commit message</param>
        public void Commit(string? message = null)
        {
            if (m_state == TransactionState.Disposed)
                throw new InvalidOperationException("Transaction is disposed");
            if (m_state != TransactionState.Open)
                throw new InvalidOperationException($"Transaction is not open: {m_state}");

            m_state = TransactionState.Committed;
            if (m_transaction != null)
                m_manager.CommitTransaction(message, false);
        }

        /// <summary>
        /// Commits the transaction and restarts it
        /// </summary>
        /// <param name="message">The commit message</param>
        public void CommitAndRestart(string? message = null)
        {
            if (m_state == TransactionState.Disposed)
                throw new InvalidOperationException("Transaction is disposed");
            if (m_state != TransactionState.Open)
                throw new InvalidOperationException($"Transaction is not open: {m_state}");

            m_state = TransactionState.Committed;
            if (m_transaction != null)
                m_manager.CommitTransaction(message, true);

            m_state = TransactionState.Open;
        }

        /// <summary>
        /// Rolls back the transaction, if it is active
        /// </summary>
        public void SafeRollback()
        {
            if (m_state == TransactionState.Disposed)
                return;
            if (m_state != TransactionState.Open)
                return;

            m_state = TransactionState.RolledBack;
            if (m_transaction != null)
                m_manager.RollbackTransaction();
        }

        /// <summary>
        /// Rolls back the transaction
        /// </summary>
        public void Rollback()
        {
            if (m_state == TransactionState.Disposed)
                throw new InvalidOperationException("Transaction is disposed");
            if (m_state != TransactionState.Open)
                throw new InvalidOperationException($"Transaction is not open: {m_state}");

            m_state = TransactionState.RolledBack;
            if (m_transaction != null)
                m_manager.RollbackTransaction();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            var state = m_state;
            m_state = TransactionState.Disposed;

            if (state == TransactionState.Open && m_transaction != null)
            {
                m_manager.RollbackTransaction();
                m_transaction.Dispose();
            }
        }
    }
}
