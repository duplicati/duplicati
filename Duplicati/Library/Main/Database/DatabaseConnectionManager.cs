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

namespace Duplicati.Library.Main.Database;

/// <summary>
/// Helper class to manage a database connection
/// </summary>
public class DatabaseConnectionManager : IDisposable
{
    /// <summary>
    /// The path to the database
    /// </summary>
    private readonly string m_path;

    /// <summary>
    /// The connection to the database
    /// </summary>
    private IDbConnection? m_connection;
    /// <summary>
    /// Flag to indicate if the object has been disposed
    /// </summary>
    private bool m_disposed;
    /// <summary>
    /// Lock object to ensure thread safety
    /// </summary>
    private object m_lock = new object();

    /// <summary>
    /// Creates a new connection manager
    /// </summary>
    /// <param name="path">The path to the database</param>
    public DatabaseConnectionManager(string path)
    {
        m_path = path;
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
                lock (m_lock)
                    if (m_connection == null)
                        m_connection = CreateConnection(m_path);

            return m_connection;
        }
    }

    /// <summary>
    /// Creates and opens a new connection with a shared cache
    /// </summary>
    /// <returns>The new connection</returns>
    public DatabaseConnectionManager CreateAdditionalConnection()
    {
        var connection = (IDbConnection?)Activator.CreateInstance(SQLiteHelper.SQLiteLoader.SQLiteConnectionType)
            ?? throw new InvalidOperationException("Failed to create connection");
        connection.ConnectionString = Connection.ConnectionString + ";Cache=Shared;";
        connection.Open();

        var instance = new DatabaseConnectionManager(m_path);
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

        return c;
    }

    /// <summary>
    /// Starts a transsaction
    /// </summary>
    /// <returns>The transaction</returns>
    public IDbTransaction BeginTransaction()
        => Connection.BeginTransaction();

    /// <summary>
    /// Creates a new command, sets the command text and prepares the parameters
    /// </summary>
    /// <returns>The command</returns>
    public IDbCommand CreateCommand()
        => Connection.CreateCommand();

    /// <summary>
    /// Creates a new command, sets the command text and prepares the parameters
    /// </summary>
    /// <param name="transaction">The transaction to use</param>
    /// <returns>The command</returns>
    public IDbCommand CreateCommand(IDbTransaction transaction)
    {
        var cmd = Connection.CreateCommand();
        cmd.Transaction = transaction;
        return cmd;
    }

    /// <summary>
    /// Creates a new command, sets the command text and prepares the parameters
    /// </summary>
    /// <param name="sql">The command text to set</param>
    /// <returns>The command</returns>
    public IDbCommand CreateCommand(string sql)
    {
        var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        var parameters = sql.Count(x => x == '?');
        for (var i = 0; i < parameters; i++)
            cmd.Parameters.Add(cmd.CreateParameter());
        return cmd;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed)
            return;

        lock (m_lock)
        {
            if (m_connection != null)
            {
                m_connection.Close();
                m_connection.Dispose();
                m_connection = null;
            }

            m_disposed = true;
        }
    }
}
