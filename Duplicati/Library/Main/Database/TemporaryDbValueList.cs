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
using System.Collections.Generic;
using System.Data;
using System.Linq;

#nullable enable

namespace Duplicati.Library.Main.Database;

/// <summary>
/// A list of values that can be used in a query
/// </summary>
public class TemporaryDbValueList : IDisposable
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(TemporaryDbValueList));

    /// <summary>
    /// The command to use
    /// </summary>
    private IDbCommand? _cmd;
    /// <summary>
    /// The connection to use
    /// </summary>
    private readonly IDbConnection _con;
    /// <summary>
    /// The transaction to use, if any
    /// </summary>
    private readonly IDbTransaction? _transaction;
    /// <summary>
    /// The table to use
    /// </summary>
    private readonly string _tableName = $"TemporaryList-{Guid.NewGuid():N}";

    /// <summary>
    /// Flag indicating if the object has been disposed
    /// </summary>
    private bool _disposed = false;
    /// <summary>
    /// Flag indicating if the values are in a table
    /// </summary>
    private bool _isTable = false;
    /// <summary>
    /// The values to use
    /// </summary>
    private IEnumerable<object> _values;
    /// <summary>
    /// The type of the values
    /// </summary>
    private readonly string _valuesType;

    /// <summary>
    /// Creates a new reusable transaction
    /// </summary>
    /// <param name="con">The connection to use</param>
    /// <param name="transaction">The transaction to use</param>
    /// <param name="values">The values to use</param>
    public TemporaryDbValueList(IDbConnection con, IDbTransaction? transaction, IEnumerable<long> values)
    {
        _con = con;
        _transaction = transaction;
        _valuesType = "INTEGER";
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        _values = values.Cast<object>();
        if (values.Count() > LocalDatabase.CHUNK_SIZE)
            ForceToTable();
    }

    /// <summary>
    /// Creates a new reusable transaction
    /// </summary>
    /// <param name="cmd">The command to use</param>
    /// <param name="con">The connection to use</param>
    /// <param name="values">The values to use</param>
    public TemporaryDbValueList(IDbConnection con, IDbTransaction? transaction, IEnumerable<string> values)
    {
        _con = con;
        _transaction = transaction;
        _valuesType = "TEXT";
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        _values = values.Cast<object>();
        if (values.Count() > LocalDatabase.CHUNK_SIZE)
            ForceToTable();
    }

    /// <summary>
    /// The values in the list
    /// </summary>
    public IEnumerable<object> Values => _values;

    /// <summary>
    /// Flag indicating if the table has been created
    /// </summary>
    public bool IsTableCreated => _isTable;

    /// <summary>
    /// Get the in clause for the values, creating the table if needed
    /// </summary>
    /// <returns>The in clause for the values</returns>
    public string GetInClause()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TemporaryDbValueList));

        ForceToTable();

        return $@"SELECT ""Value"" FROM ""{_tableName}""";
    }

    /// <summary>
    /// Force the values to be written to a table, if not already done
    /// </summary>
    public void ForceToTable()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TemporaryDbValueList));

        if (_isTable)
            return;

        _cmd = _con.CreateCommand(_transaction);
        _cmd.ExecuteNonQuery($@"CREATE TEMPORARY TABLE ""{_tableName}"" (""Value"" {_valuesType})");
        _isTable = true;
        foreach (var slice in _values.Chunk(LocalDatabase.CHUNK_SIZE))
        {
            var parameterNames = slice.Select((_, i) => $"@p{i}").ToArray();
            var sql = $@"INSERT INTO ""{_tableName}"" (""Value"") VALUES {string.Join(", ", parameterNames.Select(p => $"({p})"))}";

            _cmd.CommandText = sql;
            for (int i = 0; i < slice.Length; i++)
                _cmd.AddNamedParameter(parameterNames[i], slice[i]);

            _cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The table name to use
    /// </summary>
    public string TableName => _tableName;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (!_isTable)
            return;

        try
        {
            if (_cmd != null)
                _cmd.ExecuteNonQuery($@"DROP TABLE ""{_tableName}""");
            _cmd?.Dispose();
            _cmd = null;
        }
        catch (Exception ex)
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "DropTableFailed", ex, "Failed to drop temporary table {0}: {1}", _tableName, ex.Message);
        }
    }
}
