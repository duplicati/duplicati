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
using System.Linq;
using System.Threading;

namespace Duplicati.Library.Main.Database;

/// <summary>
/// Helper class to manage a database connection
/// </summary>
public sealed partial class DatabaseConnectionManager : IDisposable
{
    /// <summary>
    /// The tag used for log messages
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<DatabaseConnectionManager>();

    /// <summary>
    /// The path to the database
    /// </summary>
    private readonly string m_path;

    /// <summary>
    /// Flag to indicate if this connection is not using transactions
    /// </summary>
    private readonly bool m_transactionFree;

    /// <summary>
    /// The connection to the database
    /// </summary>
    private IDbConnection? m_connection;
    /// <summary>
    /// Flag to indicate if the object has been disposed
    /// </summary>
    private bool m_disposed;
    /// <summary>
    /// Flag to indicate if the vacuum command has been executed
    /// </summary>
    private bool m_hasExecutedVacuum;
    /// <summary>
    /// Lock object to ensure thread safety
    /// </summary>
    private readonly ReaderWriterLockSlim m_lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    /// <summary>
    /// The current transaction
    /// </summary>
    private DatabaseTransaction? m_transaction;

    /// <summary>
    /// Creates a new connection manager
    /// </summary>
    /// <param name="path">The path to the database</param>
    public DatabaseConnectionManager(string path)
        : this(path, false)
    { }

    /// <summary>
    /// Creates a new connection manager
    /// </summary>
    /// <param name="path">The path to the database</param>
    /// <param name="transactionFree">Flag to indicate if this connection is not using transactions</param>
    public DatabaseConnectionManager(string path, bool transactionFree)
    {
        m_path = path;
        m_transactionFree = transactionFree;
    }

    /// <summary>
    /// Gets a value indicating if the database exists
    /// </summary>
    public bool Exists => m_connection != null || System.IO.File.Exists(m_path);

    /// <summary>
    /// Gets the path to the database
    /// </summary>
    public string Path => m_path;

    /// <summary>
    /// Gets the connection or creates a new one if it does not exist
    /// </summary>
    public IDbConnection Connection
    {
        get
        {
            if (m_connection == null)
            {
                m_lock.EnterWriteLock();
                try
                {
                    if (m_connection == null)
                        m_connection = CreateConnection(m_path);
                }
                finally
                {
                    m_lock.ExitWriteLock();
                }
            }
            return m_connection;
        }
    }

    /// <summary>
    /// Creates a new connection manager that does not use transactions
    /// </summary>
    /// <returns>The new connection manager</returns>
    public DatabaseConnectionManager CreateTransactionFreeConnection()
    {
        return new DatabaseConnectionManager(m_path, true);
    }

    /// <summary>
    /// Creates and opens a new connection with a shared cache
    /// </summary>
    /// <param name="transactionFree">Flag to indicate if this connection is not using transactions</param>
    /// <returns>The new connection</returns>
    public DatabaseConnectionManager CreateAdditionalConnection(bool transactionFree)
    {
        var connection = (IDbConnection?)Activator.CreateInstance(SQLiteHelper.SQLiteLoader.SQLiteConnectionType)
            ?? throw new InvalidOperationException("Failed to create connection");
        connection.ConnectionString = Connection.ConnectionString + ";Cache=Shared;";
        connection.Open();

        // TODO: Consider if we should honor the user provided pragma settings here?

        var instance = new DatabaseConnectionManager(m_path, transactionFree);
        instance.m_connection = connection;
        return instance;
    }

    /// <summary>
    /// Creates a new connection
    /// </summary>
    /// <param name="path">The path to the database</param>
    /// <returns>The new connection</returns>
    private static IDbConnection CreateConnection(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var folder = System.IO.Path.GetDirectoryName(path);
        if (!System.IO.Directory.Exists(folder) && !string.IsNullOrEmpty(folder))
            System.IO.Directory.CreateDirectory(folder);

        var c = SQLiteHelper.SQLiteLoader.LoadConnection(path);

        try
        {
            SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(c, path, typeof(LocalDatabase));
        }
        catch
        {
            //Don't leak database connections when something goes wrong
            c.Dispose();
            throw;
        }

        // using (var cmd = c.CreateCommand())
        //     cmd.ExecuteNonQuery("PRAGMA journal_mode = WAL;");

        return c;
    }

    /// <summary>
    /// Starts a root transaction
    /// </summary>
    /// <returns>The transaction</returns>
    public DatabaseTransaction BeginRootTransaction()
        => BeginTransaction(true);

    /// <summary>
    /// Starts a transaction
    /// </summary>
    /// <returns>The transaction</returns>
    public DatabaseTransaction BeginTransaction()
        => BeginTransaction(false);

    /// <summary>
    /// Gets a value indicating if a transaction is active
    /// </summary>
    public bool IsTransactionActive => m_transaction != null;

    /// <summary>
    /// Gets a value indicating if this connection is not using transactions
    /// </summary>
    public bool IsTransactionFree => m_transactionFree;

    /// <summary>
    /// Starts a transsaction
    /// </summary>
    /// <returns>The transaction</returns>
    private DatabaseTransaction BeginTransaction(bool root)
    {
        if (m_transactionFree)
            throw new InvalidOperationException("This connection does not support transactions");

        m_lock.EnterWriteLock();
        try
        {
            if (m_transaction == null)
                return m_transaction = new DatabaseTransaction(this, Connection.BeginTransaction());
            else if (root)
                throw new InvalidOperationException("Root transaction already started");

            // SQLite does not support nested transactions, so return a dummy one
            return new DatabaseTransaction(this, null);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Executes a pragma command on the connection
    /// </summary>
    /// <param name="pragma"></param>
    public void ExecutePragma(string pragma)
    {
        m_lock.EnterWriteLock();
        try
        {
            using (var cmd = CreateCommand())
                cmd.ExecuteNonQuery($"PRAGMA {pragma}");
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Executes a pragma command on the connection
    /// </summary>
    public void ExecuteVacuum()
    {
        m_lock.EnterWriteLock();
        try
        {
            m_hasExecutedVacuum = true;
            using (var cmd = CreateCommand())
                cmd.ExecuteNonQuery($"VACUUM");
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Creates a new command, sets the command text and prepares the parameters
    /// </summary>
    /// <returns>The command</returns>
    public DatabaseCommand CreateCommand()
        => new DatabaseCommand(this);

    /// <summary>
    /// Creates a new command, sets the command text and prepares the parameters
    /// </summary>
    /// <param name="sql">The command text to set</param>
    /// <returns>The command</returns>
    public DatabaseCommand CreateCommand(string sql)
    {
        var cmd = CreateCommand();
        cmd.CommandText = sql;
        cmd.AddParameters(sql.Count(x => x == '?'));
        return cmd;
    }

    /// <summary>
    /// Prepares a command for execution
    /// </summary>
    /// <param name="cmd">The command to prepare</param>
    /// <returns>The prepared command</returns>
    /// <remarks>Only call this method while holding the lock</remarks>
    private IDbCommand PrepareCommand(IDbCommand cmd)
    {
        if (m_transactionFree)
            return cmd;

        if (m_transaction == null)
            throw new InvalidOperationException("No transaction started on connection");

        cmd.Transaction = m_transaction.Transaction;
        return cmd;
    }

    /// <summary>
    /// Executes a command and returns the number of rows affected
    /// </summary>
    /// <param name="cmd">The command to execute</param>
    /// <returns>The number of rows affected</returns>
    private int ExecuteNonQuery(IDbCommand cmd)
    {
        m_lock.EnterWriteLock();
        try
        {
            PrepareCommand(cmd);
            return cmd.ExecuteNonQuery();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Executes a command and returns the first column of the first row
    /// </summary>
    /// <param name="cmd">The command to execute</param>
    /// <returns>The first column of the first row</returns>
    private object? ExecuteScalar(IDbCommand cmd)
    {
        m_lock.EnterReadLock();
        try
        {
            PrepareCommand(cmd);
            return cmd.ExecuteScalar();
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Executes a command and returns a reader without retaining the lock.
    /// This can be used for cases where a reader is enumerating and an
    /// update to the database is made during the enumeration.
    /// Beware that SQLite does not guarantee that the data read is not affected by the update.
    /// </summary>
    /// <param name="cmd">The command to execute</param>
    /// <returns>The reader</returns>
    private DatabaseReader ExecuteReaderWithoutLocks(IDbCommand cmd)
    {
        m_lock.EnterReadLock();
        try
        {
            PrepareCommand(cmd);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
        return new DatabaseReader(this, cmd.ExecuteReader(), false);
    }

    /// <summary>
    /// Executes a command and returns a reader
    /// </summary>
    /// <param name="cmd">The command to execute</param>
    /// <returns>The reader</returns>
    private DatabaseReader ExecuteReader(IDbCommand cmd)
    {
        m_lock.EnterReadLock();
        DatabaseReader reader;
        try
        {
            PrepareCommand(cmd);
            reader = new DatabaseReader(this, cmd.ExecuteReader(), true);
        }
        catch
        {
            m_lock.ExitReadLock();
            throw;
        }

        // Dont release the lock until the reader is disposed
        return reader;
    }

    /// <summary>
    /// Releases a reader
    /// </summary>
    /// <param name="reader">The reader to release</param>
    private void ReleaseReader(DatabaseReader reader)
    {
        m_lock.ExitReadLock();
    }

    /// <summary>
    /// Commits the current transaction
    /// </summary>
    /// <param name="message">The message to log</param>
    public void CommitTransaction(string? message = null)
        => CommitTransaction(message, false);

    /// <summary>
    /// Commits the current transaction
    /// </summary>
    /// <param name="message">The message to log</param>
    public void CommitAndRestartTransaction(string? message = null)
        => CommitTransaction(message, true);

    /// <summary>
    /// Commits the current transaction
    /// </summary>
    /// <param name="message">The message to log</param>
    /// <param name="restart">A value indicating if the transaction should be restarted</param>
    private void CommitTransaction(string? message, bool restart)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (m_transaction?.Transaction == null)
                throw new InvalidOperationException("No transaction to commit?");

            using (string.IsNullOrWhiteSpace(message) ? null : new Logging.Timer(LOGTAG, "CommitTransactionAsync", message))
                m_transaction.Transaction.Commit();

            if (restart)
            {
                m_transaction.Transaction.Dispose();
                m_transaction.Transaction = Connection.BeginTransaction();
            }
            else
            {
                m_transaction = null;
            }
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Rolls back the current transaction
    /// </summary>
    public void RollbackTransaction()
    {
        m_lock.EnterWriteLock();
        try
        {
            if (m_transaction?.Transaction == null)
                throw new InvalidOperationException("No transaction to rollback?");

            m_transaction.Transaction.Rollback();
            m_transaction = null;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_lock.EnterWriteLock();
        try
        {
            if (m_transaction != null)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "DisposeConnectionManager", null, "Transaction not committed, rolling back");
                m_transaction.Transaction?.Dispose();
                m_transaction = null;
            }

            if (m_connection != null)
            {
                if (!m_hasExecutedVacuum)
                {
                    try
                    {
                        // SQLite recommends that PRAGMA optimize is run just before closing each database connection.
                        using (var tr = m_connection.BeginTransaction())
                        using (var cmd = m_connection.CreateCommand())
                        {
                            cmd.ExecuteNonQuery("PRAGMA optimize");
                            tr.Commit();
                        }
                    }
                    catch (System.Data.SQLite.SQLiteException ex)
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "FailedToOptimize", ex, "Failed to optimize database");
                    }
                }

                m_connection.Close();
                m_connection.Dispose();
                m_connection = null;

            }

            m_disposed = true;

        }
        finally
        {
            m_lock.ExitWriteLock();
            m_lock.Dispose();
        }
    }
}
