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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

#nullable enable

namespace Duplicati.Library.Main.Database;

/// <summary>
/// Extension method for <see cref="IDbCommand"/>
/// </summary>
public static partial class ExtensionMethods
{

    /// <summary>
    /// Converts the value at the given index in the reader to an Int64.
    /// </summary>
    /// <param name="reader">The <see cref="SqliteDataReader"/> to read from.</param>
    /// <param name="index">The index of the value to convert.</param>
    /// <param name="defaultvalue">The default value to return if the value is null or cannot be converted.</param>
    /// <returns>The converted Int64 value, or the default value if the conversion fails.</returns>
    public static long ConvertValueToInt64(this SqliteDataReader reader, int index, long defaultvalue = -1)
    {
        try
        {
            if (!reader.IsDBNull(index))
                return reader.GetInt64(index);
        }
        catch { }

        return defaultvalue;
    }

    /// <summary>
    /// Converts the value at the given index in the reader to a string.
    /// </summary>
    /// <param name="reader">The <see cref="SqliteDataReader"/> to read from.</param>
    /// <param name="index">The index of the value to convert.</param>
    /// <returns>The converted string value, or null if the value is null or cannot be converted.</returns>
    public static string? ConvertValueToString(this SqliteDataReader reader, int index)
    {
        var v = reader.GetValue(index);
        if (v == null || v == DBNull.Value)
            return null;

        return v.ToString();
    }

    /// <summary>
    /// Creates a new <see cref="SqliteCommand"/> with the given command text.
    /// </summary>
    /// <param name="self">The <see cref="SqliteConnection"/> to create the command for.</param>
    /// <param name="cmdtext">The command text to set for the command.</param>
    /// <returns>A new <see cref="SqliteCommand"/> with the command text set.</returns>
    public static SqliteCommand CreateCommand(this SqliteConnection self, string cmdtext)
    {
        return CreateCommandAsync(self, cmdtext, default).Await();
    }

    /// <summary>
    /// Creates a new <see cref="SqliteCommand"/> with the given command text and prepares it asynchronously.
    /// </summary>
    /// <param name="self">The <see cref="SqliteConnection"/> to create the command for.</param>
    /// <param name="cmdtext">The command text to set for the command.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A new <see cref="SqliteCommand"/> with the command text set and prepared asynchronously.</returns>
    public static async Task<SqliteCommand> CreateCommandAsync(this SqliteConnection self, string cmdtext, CancellationToken cancellationToken)
    {
        var cmd = self.CreateCommand()
            .SetCommandAndParameters(cmdtext);

        await cmd.PrepareAsync(cancellationToken).ConfigureAwait(false);

        return cmd;
    }

    /// <summary>
    /// Creates a new <see cref="SqliteCommand"/> with the given transaction.
    /// </summary>
    /// <param name="self">The <see cref="SqliteConnection"/> to create the command for.</param>
    /// <param name="transaction">The <see cref="SqliteTransaction"/> to set for the command.</param>
    /// <returns>A new <see cref="SqliteCommand"/> with the transaction set.</returns>
    public static SqliteCommand CreateCommand(this SqliteConnection self, SqliteTransaction transaction)
    {
        var cmd = self.CreateCommand();
        cmd.SetTransaction(transaction);

        return cmd;
    }

    /// <summary>
    /// Creates a new <see cref="SqliteCommand"/> with the given reusable transaction.
    /// </summary>
    /// <param name="self">The <see cref="SqliteConnection"/> to create the command for.</param>
    /// <param name="rtr">The <see cref="ReusableTransaction"/> to set for the command.</param>
    /// <returns>A new <see cref="SqliteCommand"/> with the transaction set.</returns>
    internal static SqliteCommand CreateCommand(this SqliteConnection self, ReusableTransaction rtr)
    {
        return self.CreateCommand(rtr.Transaction);
    }

    /// <summary>
    /// Executes the command asynchronously and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmdtext">The command text to execute.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the number of rows affected.</returns>
    public static async Task<int> ExecuteNonQueryAsync(this SqliteCommand self, string? cmdtext, CancellationToken cancellationToken)
    {
        return await ExecuteNonQueryAsync(self, true, cmdtext, null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmd">The command text to execute.</param>
    /// <param name="values">The values to use as parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the number of rows affected.</returns>
    public static async Task<int> ExecuteNonQueryAsync(this SqliteCommand self, string cmd, Dictionary<string, object?> values, CancellationToken cancellationToken)
    {
        return await ExecuteNonQueryAsync(self, true, cmd, values, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the number of rows affected.</returns>
    public static async Task<int> ExecuteNonQueryAsync(this SqliteCommand self, bool writeLog, CancellationToken cancellationToken)
    {
        return await ExecuteNonQueryAsync(self, writeLog, null, null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cmd">The command text to execute.</param>
    /// <param name="values">The values to set as parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the number of rows affected.</returns>
    public static async Task<int> ExecuteNonQueryAsync(this SqliteCommand self, bool writeLog, string? cmd, Dictionary<string, object?>? values, CancellationToken cancellationToken)
    {
        if (cmd != null)
            self.SetCommandAndParameters(cmd);

        if (values != null && values.Count > 0)
            self.SetParameterValues(values);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteNonQueryAsync", string.Format("ExecuteNonQueryAsync: {0}", self.GetPrintableCommandText())) : null)
            return await self.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns a <see cref="SqliteDataReader"/>.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmdtext">The command text to execute.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the <see cref="SqliteDataReader"/>.</returns>
    public static async Task<SqliteDataReader> ExecuteReaderAsync(this SqliteCommand self, string cmdtext, CancellationToken cancellationToken)
    {
        return await ExecuteReaderAsync(self, true, cmdtext, null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns a <see cref="SqliteDataReader"/>.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the <see cref="SqliteDataReader"/>.</returns>
    public static async Task<SqliteDataReader> ExecuteReaderAsync(this SqliteCommand self, bool writeLog, CancellationToken cancellationToken)
    {
        return await ExecuteReaderAsync(self, writeLog, null, null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns a <see cref="SqliteDataReader"/>.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmdtext">The command text to execute.</param>
    /// <param name="values">The values to use as parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the <see cref="SqliteDataReader"/>.</returns>
    public static async Task<SqliteDataReader> ExecuteReaderAsync(this SqliteCommand self, string cmdtext, Dictionary<string, object?>? values, CancellationToken cancellationToken)
    {
        return await ExecuteReaderAsync(self, true, cmdtext, values, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns a <see cref="SqliteDataReader"/>.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cmdtext">The command text to execute.</param>
    /// <param name="values">The values to set as parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the <see cref="SqliteDataReader"/>.</returns>
    public static async Task<SqliteDataReader> ExecuteReaderAsync(this SqliteCommand self, bool writeLog, string? cmdtext, Dictionary<string, object?>? values, CancellationToken cancellationToken)
    {
        if (cmdtext != null)
            self.SetCommandAndParameters(cmdtext);

        if (values != null && values.Count > 0)
            self.SetParameterValues(values);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteReader", string.Format("ExecuteReader: {0}", self.GetPrintableCommandText())) : null)
            return await self.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns an enumerable of <see cref="SqliteDataReader"/>.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>An asynchronous enumerable of <see cref="SqliteDataReader"/>.</returns>
    public static IAsyncEnumerable<SqliteDataReader> ExecuteReaderEnumerableAsync(this SqliteCommand self, CancellationToken cancellationToken)
    {
        return ExecuteReaderEnumerableAsync(self, true, null, null, cancellationToken);
    }

    /// <summary>
    /// Executes the command asynchronously and returns an enumerable of <see cref="SqliteDataReader"/>.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmdtext">The command text to execute.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>An asynchronous enumerable of <see cref="SqliteDataReader"/>.</returns>
    public static IAsyncEnumerable<SqliteDataReader> ExecuteReaderEnumerableAsync(this SqliteCommand self, string cmdtext, CancellationToken cancellationToken)
    {
        return ExecuteReaderEnumerableAsync(self, true, cmdtext, null, cancellationToken);
    }

    /// <summary>
    /// Executes the command asynchronously and returns an enumerable of <see cref="SqliteDataReader"/>.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmdtext">The command text to execute.</param>
    /// <param name="values">The values to use as parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>An asynchronous enumerable of <see cref="SqliteDataReader"/>.</returns>
    public static IAsyncEnumerable<SqliteDataReader> ExecuteReaderEnumerableAsync(this SqliteCommand self, string cmdtext, Dictionary<string, object?>? values, CancellationToken cancellationToken)
    {
        return ExecuteReaderEnumerableAsync(self, true, cmdtext, values, cancellationToken);
    }

    /// <summary>
    /// Executes the command asynchronously and returns an enumerable of <see cref="SqliteDataReader"/>.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cmdtext">The command text to execute.</param>
    /// <param name="values">The values to set as parameters.</param>
    /// <returns>An asynchronous enumerable of <see cref="SqliteDataReader"/>.</returns>
    public static async IAsyncEnumerable<SqliteDataReader> ExecuteReaderEnumerableAsync(this SqliteCommand self, bool writeLog, string? cmdtext, Dictionary<string, object?>? values, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cmdtext != null)
            self.SetCommandAndParameters(cmdtext);

        if (values != null && values.Count > 0)
            self.SetParameterValues(values);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteReaderEnumerableAsync", $"ExecuteReaderEnumerableAsync: {self.GetPrintableCommandText()}") : null)
        await using (var rd = await self.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await rd.ReadAsync(cancellationToken).ConfigureAwait(false))
                yield return rd;
    }

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set, or null if no rows are returned.</returns>
    public static async Task<object?> ExecuteScalarAsync(this SqliteCommand self, CancellationToken cancellationToken)
    {
        return await self.ExecuteScalarAsync(true, null, null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmdtext">The command text to execute.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set, or null if no rows are returned.</returns>
    public static async Task<object?> ExecuteScalarAsync(this SqliteCommand self, string cmdtext, CancellationToken cancellationToken)
    {
        return await self.ExecuteScalarAsync(true, cmdtext, null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set, or null if no rows are returned.</returns>
    public static async Task<object?> ExecuteScalarAsync(this SqliteCommand self, bool writeLog, CancellationToken cancellationToken)
    {
        return await self.ExecuteScalarAsync(writeLog, null, null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmdtext">The command text to execute.</param>
    /// <param name="values">The values to use as parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set, or null if no rows are returned.</returns>
    public static async Task<object?> ExecuteScalarAsync(this SqliteCommand self, string cmdtext, Dictionary<string, object?>? values, CancellationToken cancellationToken)
    {
        return await self.ExecuteScalarAsync(true, cmdtext, values, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cmd">The command text to execute.</param>
    /// <param name="values">The values to set as parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set, or null if no rows are returned.</returns>
    public static async Task<object?> ExecuteScalarAsync(this SqliteCommand self, bool writeLog, string? cmd, Dictionary<string, object?>? values, CancellationToken cancellationToken)
    {
        if (cmd != null)
            self.SetCommandAndParameters(cmd);

        if (values != null && values.Count > 0)
            self.SetParameterValues(values);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteScalarAsync", string.Format("ExecuteScalarAsync: {0}", self.GetPrintableCommandText())) : null)
            return await self.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or -1 if no rows are returned or the value cannot be converted.</returns>
    public static Task<long> ExecuteScalarInt64Async(this SqliteCommand self, CancellationToken cancellationToken)
        => ExecuteScalarInt64Async(self, true, null, null, -1, cancellationToken);

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="defaultvalue">The default value to return if no rows are returned or the value cannot be converted.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or -1 if no rows are returned or the value cannot be converted.</returns>
    public static Task<long> ExecuteScalarInt64Async(this SqliteCommand self, long defaultvalue, CancellationToken cancellationToken)
        => ExecuteScalarInt64Async(self, true, null, null, defaultvalue, cancellationToken);

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or -1 if no rows are returned or the value cannot be converted.</returns>
    public static Task<long> ExecuteScalarInt64Async(this SqliteCommand self, bool writeLog, CancellationToken cancellationToken)
        => ExecuteScalarInt64Async(self, writeLog, null, null, -1, cancellationToken);

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="defaultvalue">The default value to return if no rows are returned or the value cannot be converted.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or the default value if no rows are returned or the value cannot be converted.</returns>
    public static Task<long> ExecuteScalarInt64Async(this SqliteCommand self, bool writeLog, long defaultvalue, CancellationToken cancellationToken)
        => ExecuteScalarInt64Async(self, writeLog, null, null, defaultvalue, cancellationToken);

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmd">The command text to execute.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or the default value if no rows are returned or the value cannot be converted.</returns>
    public static Task<long> ExecuteScalarInt64Async(this SqliteCommand self, string? cmd, CancellationToken cancellationToken)
        => ExecuteScalarInt64Async(self, true, cmd, null, -1, cancellationToken);

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmd">The command text to execute.</param>
    /// <param name="defaultvalue">The default value to return if no rows are returned or the value cannot be converted.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or the default value if no rows are returned or the value cannot be converted.</returns>
    public static Task<long> ExecuteScalarInt64Async(this SqliteCommand self, string? cmd, long defaultvalue, CancellationToken cancellationToken)
        => ExecuteScalarInt64Async(self, true, cmd, null, defaultvalue, cancellationToken);

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmd">The command text to execute.</param>
    /// <param name="values">The values to use as parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or the default value if no rows are returned or the value cannot be converted.</returns>
    public static Task<long> ExecuteScalarInt64Async(this SqliteCommand self, string? cmd, Dictionary<string, object?>? values, CancellationToken cancellationToken)
        => ExecuteScalarInt64Async(self, true, cmd, values, -1, cancellationToken);

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="cmd">The command text to execute.</param>
    /// <param name="values">The values to use as parameters.</param>
    /// <param name="defaultvalue">The default value to return if no rows are returned or the value cannot be converted.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or the default value if no rows are returned or the value cannot be converted.</returns>
    public static Task<long> ExecuteScalarInt64Async(this SqliteCommand self, string? cmd, Dictionary<string, object?>? values, long defaultvalue, CancellationToken cancellationToken)
        => ExecuteScalarInt64Async(self, true, cmd, values, defaultvalue, cancellationToken);

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cmd">The command text to execute.</param>
    /// <param name="values">The values to set as parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or the default value if no rows are returned or the value cannot be converted.</returns>
    public static Task<long> ExecuteScalarInt64Async(this SqliteCommand self, bool writeLog, string? cmd, Dictionary<string, object?>? values, CancellationToken cancellationToken)
        => ExecuteScalarInt64Async(self, writeLog, cmd, values, -1, cancellationToken);

    /// <summary>
    /// Executes the command asynchronously and returns the first column of the first row in the result set as an Int64.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> instance to execute on.</param>
    /// <param name="writeLog">Whether to write a log entry.</param>
    /// <param name="cmd">The command text to execute.</param>
    /// <param name="values">The values to set as parameters.</param>
    /// <param name="defaultvalue">The default value to return if no rows are returned or the value cannot be converted.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the first column of the first row in the result set as an Int64, or the default value if no rows are returned or the value cannot be converted.</returns>
    public static async Task<long> ExecuteScalarInt64Async(this SqliteCommand self, bool writeLog, string? cmd, Dictionary<string, object?>? values, long defaultvalue, CancellationToken cancellationToken)
    {
        if (cmd != null)
            self.SetCommandAndParameters(cmd);

        if (values != null && values.Count > 0)
            self.SetParameterValues(values);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteScalarInt64Async", string.Format("ExecuteScalarInt64Async: {0}", self.GetPrintableCommandText())) : null)
        await using (var rd = await self.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            if (await rd.ReadAsync(cancellationToken).ConfigureAwait(false))
                return ConvertValueToInt64(rd, 0, defaultvalue);

        return defaultvalue;
    }

    /// <summary>
    /// Expands an IN clause parameter in a SqliteCommand by replacing the parameter with multiple parameters for each value in the collection.
    /// </summary>
    /// <param name="cmd">The <see cref="SqliteCommand"/> to expand the IN clause parameter for.</param>
    /// <param name="originalParamName">The name of the original parameter to replace, must start with '@'.</param>
    /// <param name="values">The collection of values to expand the IN clause with.</param>
    /// <typeparam name="T">The type of the values in the collection.</typeparam>
    /// <returns>The modified <see cref="SqliteCommand"/> with the IN clause expanded.</returns>
    public static SqliteCommand ExpandInClauseParameterMssqlite<T>(this SqliteCommand cmd, string originalParamName, IEnumerable<T> values)
    {
        if (string.IsNullOrWhiteSpace(originalParamName) || !originalParamName.StartsWith("@"))
            throw new ArgumentException("Parameter name must start with '@'", nameof(originalParamName));

        foreach (var p in cmd.Parameters)
            if (p is IDataParameter parameter && parameter.ParameterName.Equals(originalParamName, StringComparison.OrdinalIgnoreCase))
            {
                cmd.Parameters.Remove(parameter);
                break;
            }

        var inClause = string.Join(", ", values.Select((value, index) =>
        {
            var param_name = Library.Utility.Utility.FormatInvariantFormattable($"{originalParamName}{index}");
            cmd.AddNamedParameter(param_name, value);
            return param_name;
        }));

        if (string.IsNullOrWhiteSpace(inClause) && values.Any())
            throw new ArgumentException("IN clause cannot be empty", nameof(values));

#if DEBUG
        if (!cmd.CommandText.Contains(originalParamName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Command text does not contain parameter '{originalParamName}'", nameof(originalParamName));
#endif
        cmd.CommandText = cmd.CommandText.Replace(originalParamName, inClause, StringComparison.OrdinalIgnoreCase);

        return cmd;
    }

    /// <summary>
    /// Expands an IN clause parameter in a SqliteCommand by replacing the parameter with multiple parameters for each value in the collection.
    /// If a temporary table is used, it replaces the parameter with the table name.
    /// </summary>
    /// <param name="cmd">The <see cref="SqliteCommand"/> to expand the IN clause parameter for.</param>
    /// <param name="originalParamName">The name of the original parameter to replace, must start with '@'.</param>
    /// <param name="values">The collection of values to expand the IN clause with.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that when awaited contains the modified <see cref="SqliteCommand"/> with the IN clause expanded.</returns>
    internal static async Task<SqliteCommand> ExpandInClauseParameterMssqliteAsync(this SqliteCommand cmd, string originalParamName, TemporaryDbValueList values, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(originalParamName) || !originalParamName.StartsWith("@"))
            throw new ArgumentException("Parameter name must start with '@'", nameof(originalParamName));

        if (!values.IsTableCreated)
            return ExpandInClauseParameterMssqlite(cmd, originalParamName, values.Values);

        // We have a temporary table, so we need to replace the parameter with the table name
        cmd.CommandText = cmd.CommandText.Replace(
            originalParamName,
            await values.GetInClause(cancellationToken).ConfigureAwait(false),
            StringComparison.OrdinalIgnoreCase
        );

        return cmd;
    }

    /// <summary>
    /// Sets the command text and parameters for the given <see cref="SqliteCommand"/>.
    /// </summary>
    /// <param name="cmd">The <see cref="SqliteCommand"/> to set the command text and parameters for.</param>
    /// <param name="cmdtext">The command text to set for the command, must not contain '?' as a parameter placeholder.</param>
    /// <returns>The modified <see cref="SqliteCommand"/> with the command text and parameters set.</returns>
    /// <exception cref="ArgumentException">If the command text contains '?' as a parameter placeholder.</exception>
    public static SqliteCommand SetCommandAndParameters(this SqliteCommand cmd, string cmdtext)
    {
        cmd.CommandText = cmdtext;
        cmd.Parameters.Clear();

#if DEBUG
        if (cmd.CommandText.Contains('?', StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Command text cannot contain '?' as a parameter placeholder, use '@' instead.", nameof(cmdtext));
#endif

        var parameters = MyRegex().Matches(cmdtext);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in parameters)
        {
            if (found.Contains(match.Value))
                continue;
            found.Add(match.Value);
            cmd.AddNamedParameter(match.Value);
        }

        return cmd;
    }

    /// <summary>
    /// Sets the value of a parameter in the command.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> to set the parameter value for.</param>
    /// <param name="name">The name of the parameter to set the value for.</param>
    /// <param name="value">The value to set for the parameter, or null to set it to DBNull.Value.</param>
    /// <returns>The <see cref="SqliteCommand"/> with the parameter value set.</returns>
    public static SqliteCommand SetParameterValue(this SqliteCommand self, string name, object? value)
    {
#if DEBUG
        if (value is not null && value is System.Collections.IEnumerable && value is not string)
            throw new ArgumentException($"Cannot set parameter '{name}' to an array or enumerable type, as the SQLite bindings does not support it.", nameof(value));
#endif
        self.Parameters[name].Value = value ?? DBNull.Value;

        return self;
    }

    // Special case for DateTime, as we need to convert it to a long
    /// <summary>
    /// Sets the value of a DateTime parameter in the command.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> to set the parameter value for.</param>
    /// <param name="name">The name of the parameter to set the value for.</param>
    /// <param name="value">The DateTime value to set for the parameter.</param>
    /// <returns>The <see cref="SqliteCommand"/> with the parameter value set.</returns>
    public static SqliteCommand SetParameterValue(this SqliteCommand self, string name, DateTime value)
    {
        self.Parameters[name].Value = Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(value);

        return self;
    }

    /// <summary>
    /// Sets the parameter values for the command, the parameters must already be added.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> to set the parameter values for.</param>
    /// <param name="values">The values to set the parameters to, where the key is the parameter name.</param>
    /// <returns>The <see cref="SqliteCommand"/> with the parameter values set.</returns>
    public static SqliteCommand SetParameterValues(this SqliteCommand self, Dictionary<string, object?> values)
    {
        foreach (var kvp in values)
            self.Parameters[kvp.Key].Value = kvp.Value ?? DBNull.Value;

        return self;
    }

    /// <summary>
    /// Sets the transaction for the command.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> to set the transaction for.</param>
    /// <param name="transaction">The <see cref="SqliteTransaction"/> to set for the command.</param>
    /// <returns>The <see cref="SqliteCommand"/> with the transaction set.</returns>
    public static SqliteCommand SetTransaction(this SqliteCommand self, SqliteTransaction transaction)
    {
        self.Transaction = transaction;

        return self;
    }

    /// <summary>
    /// Sets the transaction for the command using a <see cref="ReusableTransaction"/>.
    /// </summary>
    /// <param name="self">The <see cref="SqliteCommand"/> to set the transaction for.</param>
    /// <param name="rtr">The <see cref="ReusableTransaction"/> to set for the command.</param>
    /// <returns>The <see cref="SqliteCommand"/> with the transaction set.</returns>
    internal static SqliteCommand SetTransaction(this SqliteCommand self, ReusableTransaction rtr)
    {
        self.Transaction = rtr.Transaction;

        return self;
    }

    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ExtensionMethods));

    // This regex matches named parameters in the form of @paramName
    [GeneratedRegex(@"@\w+")]
    private static partial Regex MyRegex();

    /// <summary>
    /// Adds a parameter to the command with the given value.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="self">The command to add the parameter to</param>
    /// <param name="name">The name of the parameter</param>
    /// <param name="value">The optional value of the parameter</param>
    /// <returns>The command with the parameter added</returns>
    public static SqliteCommand AddNamedParameter(this SqliteCommand self, string name, object? value = null)
    {
        var p = self.CreateParameter();
        p.ParameterName = name;
        if (value != null)
            p.Value = value;
        self.Parameters.Add(p);
        return self;
    }

    // TODO At some point, when the entire codebase is migrated to
    // Microsoft.Data.Sqlite, the extension methods using IDb* should be
    // removed. Currently, they're kept for compatibility.

    /// <summary>
    /// Adds a named parameter to the command with the given value.
    /// </summary>
    /// <param name="self">The command to add the parameter to</param>
    /// <param name="name">The name of the parameter</param>
    /// <param name="value">The optional value of the parameter</param>
    /// <returns>The command with the parameter added</returns>
    public static IDbCommand AddNamedParameter(this IDbCommand self, string name, object? value = null)
    {
        var p = self.CreateParameter();
        p.ParameterName = name;
        if (value != null)
            p.Value = value;
        self.Parameters.Add(p);
        return self;
    }

    /// <summary>
    /// Sets the parameter values for the command, the parameters must already be added.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="self">The command to set the parameter values for</param>
    /// <param name="values">The values to set the parameters to</param>
    /// <returns>The command with the parameter values set</returns>
    public static T SetParameterValues<T>(this T self, Dictionary<string, object?> values)
        where T : IDbCommand
    {
        foreach (var kvp in values)
            ((IDataParameter)self.Parameters[kvp.Key]!).Value = kvp.Value ?? DBNull.Value;
        return self;
    }

    /// <summary>
    /// Sets the parameter value for the command at the given index.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="self">The command to set the parameter value for</param>
    /// <param name="name">The name of the parameter to set the value for</param>
    /// <param name="value">The value to set the parameter to</param>
    /// <returns>The command with the parameter value set</returns>
    public static T SetParameterValue<T>(this T self, string name, object? value)
        where T : IDbCommand
    {
#if DEBUG
        if (value is not null && value is System.Collections.IEnumerable && value is not string)
            throw new ArgumentException($"Cannot set parameter '{name}' to an array or enumerable type, as the SQLite bindings does not support it.", nameof(value));
#endif
        ((IDataParameter)self.Parameters[name]!).Value = value ?? DBNull.Value;
        return self;
    }

    /// <summary>
    /// Gets the printable command text for the given command.
    /// </summary
    /// <param name="self">The command to get the printable command text for</param>
    /// <returns>The printable command text</returns>
    public static string GetPrintableCommandText(this IDbCommand self)
    {
        var txt = self.CommandText;

        // Replace each named parameter with its value
        foreach (IDataParameter p in self.Parameters)
        {
            var paramName = p.ParameterName;
            if (string.IsNullOrEmpty(paramName))
                continue;

            string v;
            if (p.Value == null || p.Value == DBNull.Value)
                v = "NULL";
            else if (p.Value is string)
                v = Library.Utility.Utility.FormatInvariantFormattable($"\"{p.Value}\"");
            else if (p.Value is DateTime dt)
                v = Library.Utility.Utility.FormatInvariantFormattable($"\"{dt:O}\"");
            else if (p.Value is byte[] bytes)
                v = Library.Utility.Utility.FormatInvariantFormattable($"X'{BitConverter.ToString(bytes).Replace("-", "")}'");
            else
                v = Library.Utility.Utility.FormatInvariantValue(p.Value);

            // Replace all occurrences of the parameter (with word boundary)
            txt = Regex.Replace(txt, $@"\B{Regex.Escape(paramName)}\b", v, RegexOptions.IgnoreCase);
        }

        return txt;
    }

    /// <summary>
    /// Executes the command and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <returns>The number of rows affected</returns>
    public static int ExecuteNonQuery(this IDbCommand self, bool writeLog)
    {
        return ExecuteNonQuery(self, writeLog, null);
    }

    /// <summary>
    /// Executes the command and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>The number of rows affected</returns>
    public static int ExecuteNonQuery(this IDbCommand self, string cmd)
        => ExecuteNonQuery(self, true, cmd);

    /// <summary>
    /// Executes the command and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>The number of rows affected</returns>
    public static int ExecuteNonQuery(this IDbCommand self, string cmd, Dictionary<string, object?> values)
        => ExecuteNonQuery(self, true, cmd, values);

    /// <summary>
    /// Executes the command and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>The number of rows affected</returns>
    public static int ExecuteNonQuery(this IDbCommand self, bool writeLog, string? cmd, Dictionary<string, object?> values)
    {
        if (cmd != null)
            self.SetCommandAndParameters(cmd);

        if (values != null && values.Count > 0)
            self.SetParameterValues(values);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteNonQuery", string.Format("ExecuteNonQuery: {0}", self.GetPrintableCommandText())) : null)
            return self.ExecuteNonQuery();
    }

    /// <summary>
    /// Executes the command and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <returns>The number of rows affected</returns>
    public static int ExecuteNonQuery(this IDbCommand self, bool writeLog, string? cmd)
    {
        if (cmd != null)
            self.SetCommandAndParameters(cmd);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteNonQuery", string.Format("ExecuteNonQuery: {0}", self.GetPrintableCommandText())) : null)
            return self.ExecuteNonQuery();
    }

    /// <summary>
    /// Executes the command and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="transaction">The transaction to use for the command</param>
    /// <returns>The number of rows affected</returns>
    public static int ExecuteNonQuery(this IDbCommand self, IDbTransaction? transaction)
    {
        self.Transaction = transaction;
        return self.ExecuteNonQuery();
    }

    /// <summary>
    /// Executes the command and returns the scalar value of the first row.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <returns>The scalar value of the first row</returns>
    public static object? ExecuteScalar(this IDbCommand self, string cmd)
        => ExecuteScalar(self, true, cmd);

    /// <summary>
    /// Executes the command and returns the scalar value of the first row.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <returns>The scalar value of the first row</returns>
    public static object? ExecuteScalar(this IDbCommand self, bool writeLog, string? cmd)
    {
        if (cmd != null)
            self.SetCommandAndParameters(cmd);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteScalar", string.Format("ExecuteScalar: {0}", self.GetPrintableCommandText())) : null)
            return self.ExecuteScalar();
    }

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this IDbCommand self, bool writeLog, long defaultvalue = -1)
        => ExecuteScalarInt64(self, writeLog, null, defaultvalue);

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this IDbCommand self, long defaultvalue = -1)
        => ExecuteScalarInt64(self, true, null, defaultvalue);

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="transaction">The transaction to use for the command</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this IDbCommand self, IDbTransaction? transaction, long defaultvalue = -1)
    {
        self.Transaction = transaction;
        return ExecuteScalarInt64(self, true, null, defaultvalue);
    }

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this IDbCommand self, string? cmd, long defaultvalue = -1)
        => ExecuteScalarInt64(self, true, cmd, defaultvalue);

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this IDbCommand self, bool writeLog, string? cmd, long defaultvalue)
    {
        if (cmd != null)
            self.SetCommandAndParameters(cmd);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteScalarInt64", string.Format("ExecuteScalarInt64: {0}", self.GetPrintableCommandText())) : null)
        using (var rd = self.ExecuteReader())
            if (rd.Read())
                return ConvertValueToInt64(rd, 0, defaultvalue);

        return defaultvalue;
    }

    /// <summary>
    /// Executes the command and returns a data reader.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>A <see cref="IDataReader"/> instance</returns>
    public static IDataReader ExecuteReader(this IDbCommand self, string cmd, Dictionary<string, object?>? values)
        => ExecuteReader(self, true, cmd, values);


    /// <summary>
    /// Executes the command and returns a data reader.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <returns>A <see cref="IDataReader"/> instance</returns>
    public static IDataReader ExecuteReader(this IDbCommand self, string cmd)
        => ExecuteReader(self, true, cmd);

    /// <summary>
    /// Executes the command and returns a data reader.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <returns>A <see cref="IDataReader"/> instance</returns>
    public static IDataReader ExecuteReader(this IDbCommand self, bool writeLog, string? cmd)
    {
        if (cmd != null)
            self.SetCommandAndParameters(cmd);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteReader", string.Format("ExecuteReader: {0}", self.GetPrintableCommandText())) : null)
            return self.ExecuteReader();
    }

    /// <summary>
    /// Executes the command and returns a data reader.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>A <see cref="IDataReader"/> instance</returns>
    public static IDataReader ExecuteReader(this IDbCommand self, bool writeLog, string? cmd, Dictionary<string, object?>? values)
    {
        if (cmd != null)
            self.SetCommandAndParameters(cmd);

        if (values != null && values.Count > 0)
            self.SetParameterValues(values);

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteReader", string.Format("ExecuteReader: {0}", self.GetPrintableCommandText())) : null)
            return self.ExecuteReader();
    }

    /// <summary>
    /// Executes the given command string `cmd` on the given database command `self` with the given values `values` and returns an enumerable of data readers.
    /// </summary>
    /// <param name="self">The database command to execute on.</param>
    /// <param name="cmd">The command string to execute.</param>
    /// <returns></returns>
    public static IEnumerable<IDataReader> ExecuteReaderEnumerable(this IDbCommand self, string cmd)
    {
        using var rd = ExecuteReader(self, cmd);
        while (rd.Read())
            yield return rd;
    }

    /// <summary>
    /// Executes the given command string `cmd` on the given database command `self` with the given values `values` and returns an enumerable of data readers.
    /// </summary>
    /// <param name="self">The database command to execute on.</param>
    /// <param name="cmd">The command string to execute.</param>
    /// <returns></returns>
    public static IEnumerable<IDataReader> ExecuteReaderEnumerable(this IDbCommand self)
    {
        using var rd = self.ExecuteReader();
        while (rd.Read())
            yield return rd;
    }

    /// <summary>
    /// Converts the value at the given index of the given data reader to a string.
    /// </summary>
    /// <param name="reader">The data reader to convert the value from.</param>
    /// <param name="index">The index of the value to convert.</param>
    /// <returns>The value at the given index as a string.</returns>
    public static string? ConvertValueToString(this IDataReader reader, int index)
    {
        var v = reader.GetValue(index);
        if (v == null || v == DBNull.Value)
            return null;

        return v.ToString();
    }

    /// <summary>
    /// Converts the value at the given index of the given data reader to a long.
    /// </summary>
    /// <param name="reader">The data reader to convert the value from.</param>
    /// <param name="index">The index of the value to convert.</param>
    /// <param name="defaultvalue">The default value to return if the value is null or cannot be converted.</param>
    /// <returns>The value at the given index as a long.</returns>
    public static long ConvertValueToInt64(this IDataReader reader, int index, long defaultvalue = -1)
    {
        try
        {
            if (!reader.IsDBNull(index))
                return reader.GetInt64(index);
        }
        catch
        {
        }

        return defaultvalue;
    }

    /// <summary>
    /// Creates a command with the given transaction.
    /// </summary>
    /// <param name="self">The connection to create the command on.</param>
    /// <param name="transaction">The transaction to use for the command.</param>
    /// <param name="cmd">The command string to create the command with.</param>
    /// <returns>A new command with the given transaction.</returns>
    public static IDbCommand CreateCommand(this IDbConnection self, IDbTransaction? transaction, string? cmdtext = null)
    {
        var cmd = self.CreateCommand();
        cmd.Transaction = transaction;
        if (!string.IsNullOrEmpty(cmdtext))
            cmd.SetCommandAndParameters(cmdtext);
        return cmd;
    }

    /// <summary>
    /// Sets the command text and adds parameters to the command.
    /// </summary>
    /// <param name="cmd">The command to set the command text and add parameters to.</param>
    /// <param name="transaction">The transaction to use for the command.</param>
    /// <param name="cmdtext">The command text to set.</param>
    /// <returns>The command with the command text set and parameters added.</returns>
    public static IDbCommand SetCommandAndParameters(this IDbCommand cmd, IDbTransaction transaction, string cmdtext)
    {
        cmd.Transaction = transaction;
        return cmd.SetCommandAndParameters(cmdtext);
    }

    /// <summary>
    /// Sets the command text and adds parameters to the command.
    /// </summary>
    /// <param name="cmd">The command to set the command text and add parameters to.</param>
    /// <param name="cmdtext">The command text to set.</param>
    /// <returns>The command with the command text set and parameters added.</returns>
    public static IDbCommand SetCommandAndParameters(this IDbCommand cmd, string cmdtext)
    {
        cmd.CommandText = cmdtext;
        cmd.Parameters.Clear();

#if DEBUG
        if (cmd.CommandText.Contains("?", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Command text cannot contain '?' as a parameter placeholder, use '@' instead.", nameof(cmdtext));
#endif

        var parameters = Regex.Matches(cmdtext, @"@\w+");
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in parameters)
        {
            if (found.Contains(match.Value))
                continue;
            found.Add(match.Value);
            cmd.AddNamedParameter(match.Value);
        }

        return cmd;
    }

    /// <summary>
    /// Creates a command with the given command string, and adds parameters to fit the input
    /// </summary>
    /// <param name="self">The connection to create the command on</param>
    /// <param name="cmdtext">The command string to create the command with</param>
    /// <returns>The command with the parameters added</returns>
    public static IDbCommand CreateCommand(this IDbConnection self, string cmdtext)
        => CreateCommand(self, null, cmdtext);

    /// <summary>
    /// Expands the given parameter name to a list of parameters for an IN clause.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command</typeparam>
    /// <typeparam name="TValue">The type of the values</typeparam>
    /// <param name="cmd">The command to expand the parameter for</param>
    /// <param name="originalParamName">The original parameter name to expand</param>
    /// <param name="values">The values to expand the parameter for</param>
    public static IDbCommand ExpandInClauseParameter<TValue>(this IDbCommand cmd, string originalParamName, IEnumerable<TValue> values)
    {
        if (string.IsNullOrWhiteSpace(originalParamName) || !originalParamName.StartsWith("@"))
            throw new ArgumentException("Parameter name must start with '@'", nameof(originalParamName));

        foreach (var p in cmd.Parameters)
            if (p is IDataParameter parameter && parameter.ParameterName.Equals(originalParamName, StringComparison.OrdinalIgnoreCase))
            {
                cmd.Parameters.Remove(parameter);
                break;
            }

        var inClause = string.Join(", ", values.Select((value, index) =>
        {
            var param_name = Library.Utility.Utility.FormatInvariantFormattable($"{originalParamName}{index}");
            cmd.AddNamedParameter(param_name, value);
            return param_name;
        }));

        if (string.IsNullOrWhiteSpace(inClause) && values.Any())
            throw new ArgumentException("IN clause cannot be empty", nameof(values));

#if DEBUG
        if (!cmd.CommandText.Contains(originalParamName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Command text does not contain parameter '{originalParamName}'", nameof(originalParamName));
#endif
        cmd.CommandText = cmd.CommandText.Replace(originalParamName, inClause, StringComparison.OrdinalIgnoreCase);

        return cmd;
    }

    /// <summary>
    /// Expands the given parameter name to a list of parameters for an IN clause.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="cmd">The command to expand the parameter for</param>
    /// <param name="originalParamName">The original parameter name to expand</param>
    /// <param name="values">The values to expand the parameter for</param>
    internal static IDbCommand ExpandInClauseParameter(this IDbCommand cmd, string originalParamName, TemporaryDbValueList values)
    {
        if (string.IsNullOrWhiteSpace(originalParamName) || !originalParamName.StartsWith("@"))
            throw new ArgumentException("Parameter name must start with '@'", nameof(originalParamName));

        if (!values.IsTableCreated)
            return ExpandInClauseParameter(cmd, originalParamName, values.Values);

        // We have a temporary table, so we need to replace the parameter with the table name
        cmd.CommandText = cmd.CommandText.Replace(originalParamName, values.GetInClause(default).Await(), StringComparison.OrdinalIgnoreCase);
        return cmd;
    }

}