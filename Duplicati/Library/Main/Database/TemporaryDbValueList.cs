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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

#nullable enable

namespace Duplicati.Library.Main.Database;

/// <summary>
/// A list of values that can be used in a query.
/// </summary>
internal class TemporaryDbValueList : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// The tag used for logging.
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(TemporaryDbValueList));

    /// <summary>
    /// The command to use.
    /// </summary>
    private SqliteCommand? _cmd;
    /// <summary>
    /// The connection to use.
    /// </summary>
    private SqliteConnection _con;
    /// <summary>
    /// The reusable transaction to use.
    /// </summary>
    private ReusableTransaction _rtr;
    /// <summary>
    /// The table to use.
    /// </summary>
    private readonly string _tableName = $"TemporaryList-{Library.Utility.Utility.GetHexGuid()}";

    /// <summary>
    /// Flag indicating if the object has been disposed.
    /// </summary>
    private bool _disposed = false;
    /// <summary>
    /// Flag indicating if the values are in a table.
    /// </summary>
    private bool _isTable = false;
    /// <summary>
    /// The values to use.
    /// </summary>
    private readonly IEnumerable<object> _values;
    /// <summary>
    /// The type of the values.
    /// </summary>
    private readonly string _valuesType;

    /// <summary>
    /// Creates a new TemporaryDbValueList with the given values.
    /// </summary>
    /// <param name="con">The connection to use.</param>
    /// <param name="rtr">The reusable transaction to use.</param>
    /// <param name="values">The values to use.</param>
    /// <param name="valuesType">The type of the values (e.g., "INTEGER", "TEXT").</param>
    private TemporaryDbValueList(SqliteConnection con, ReusableTransaction rtr, IEnumerable<object> values, string valuesType)
    {
        _con = con;
        _rtr = rtr;
        _valuesType = valuesType;
        _values = values;

        ArgumentNullException.ThrowIfNull(values);
    }

    /// <summary>
    /// Asynchronously creates a new TemporaryDbValueList with the given values.
    /// </summary>
    /// <param name="db">The database to use.</param>
    /// <param name="values">The values to use.</param>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the created TemporaryDbValueList.</returns>
    internal static async Task<TemporaryDbValueList> CreateAsync(LocalDatabase db, IEnumerable<long> values, CancellationToken token)
    {
        return await DoCreateAsync(new TemporaryDbValueList(db.Connection, db.Transaction, values.Cast<object>(), "INTEGER"), token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously creates a new TemporaryDbValueList with the given values.
    /// </summary>
    /// <param name="db">The database to use.</param>
    /// <param name="values">The values to use.</param>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the created TemporaryDbValueList.</returns>
    internal static async Task<TemporaryDbValueList> CreateAsync(LocalDatabase db, IEnumerable<string> values, CancellationToken token)
    {
        return await DoCreateAsync(new TemporaryDbValueList(db.Connection, db.Transaction, values.Cast<object>(), "TEXT"), token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously creates a new TemporaryDbValueList with the given values.
    /// </summary>
    /// <param name="con">The connection to use.</param>
    /// <param name="rtr">The reusable transaction to use.</param>
    /// <param name="values">The values to use.</param>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the created TemporaryDbValueList.</returns>
    internal static async Task<TemporaryDbValueList> CreateAsync(SqliteConnection con, ReusableTransaction rtr, IEnumerable<long> values, CancellationToken token)
    {
        return await DoCreateAsync(new TemporaryDbValueList(con, rtr, values.Cast<object>(), "INTEGER"), token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously creates a new TemporaryDbValueList with the given values.
    /// </summary>
    /// <param name="con">The connection to use.</param>
    /// <param name="rtr">The reusable transaction to use.</param>
    /// <param name="values">The values to use.</param>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the created TemporaryDbValueList.</returns>
    internal static async Task<TemporaryDbValueList> CreateAsync(SqliteConnection con, ReusableTransaction rtr, IEnumerable<string> values, CancellationToken token)
    {
        return await DoCreateAsync(new TemporaryDbValueList(con, rtr, values.Cast<object>(), "TEXT"), token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the actual creation of the TemporaryDbValueList, ensuring that if the number of values exceeds the chunk size,
    /// the values are forced into a table.
    /// </summary>
    /// <param name="valueList">The TemporaryDbValueList to create.</param>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the created TemporaryDbValueList.</returns>
    private static async Task<TemporaryDbValueList> DoCreateAsync(TemporaryDbValueList valueList, CancellationToken token)
    {
        if (valueList._values.Count() > LocalDatabase.CHUNK_SIZE)
            await valueList.ForceToTable(token).ConfigureAwait(false);

        return valueList;
    }

    /// <summary>
    /// The values in the list.
    /// </summary>
    public IEnumerable<object> Values => _values;

    /// <summary>
    /// Flag indicating if the table has been created.
    /// </summary>
    public bool IsTableCreated => _isTable;

    /// <summary>
    /// Get the in clause for the values, creating the table if needed.
    /// </summary>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the SQL in clause string.</returns>
    public async Task<string> GetInClause(CancellationToken token)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TemporaryDbValueList));

        await ForceToTable(token).ConfigureAwait(false);

        return $@"
            SELECT ""Value""
            FROM ""{_tableName}""
        ";
    }

    /// <summary>
    /// Force the values to be written to a table, if not already done.
    /// </summary>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the table has been created and the values inserted.</returns>
    public async Task ForceToTable(CancellationToken token)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TemporaryDbValueList));

        if (_isTable)
            return;

        _cmd = _con.CreateCommand(_rtr);
        await _cmd.ExecuteNonQueryAsync($@"
            CREATE TEMPORARY TABLE ""{_tableName}"" (""Value"" {_valuesType})
        ", token)
            .ConfigureAwait(false);

        _isTable = true;
        foreach (var slice in _values.Chunk(LocalDatabase.CHUNK_SIZE))
        {
            var parameterNames = slice.Select((_, i) => $"@p{Library.Utility.Utility.FormatInvariantValue(i)}").ToArray();
            var sql = $@"
                INSERT INTO ""{_tableName}"" (""Value"")
                VALUES {string.Join(", ", parameterNames.Select(p => $"({p})"))}
            ";

            _cmd.SetCommandAndParameters(sql);
            for (int i = 0; i < slice.Length; i++)
                _cmd.SetParameterValue(parameterNames[i], slice[i]);

            await _cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The table name to use.
    /// </summary>
    public string TableName => _tableName;

    /// <inheritdoc/>
    public void Dispose()
    {
        DisposeAsync().AsTask().Await();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (!_isTable)
            return;

        try
        {
            if (_cmd != null)
            {
                await _cmd.ExecuteNonQueryAsync($@"DROP TABLE ""{_tableName}""", default)
                    .ConfigureAwait(false);
                await _cmd.DisposeAsync().ConfigureAwait(false);
            }
            _cmd = null;
        }
        catch (Exception ex)
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "DropTableFailed", ex, "Failed to drop temporary table {0}: {1}", _tableName, ex.Message);
        }
    }
}
