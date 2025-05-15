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

#nullable enable

namespace Duplicati.Library.Main.Database;

/// <summary>
/// Extension method for <see cref="IDbCommand"/>
/// </summary>
public static class ExtensionMethods
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ExtensionMethods));

    /// <summary>
    /// Adds a parameter to the command with the given value.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="self">The command to add the parameter to</param>
    /// <param name="name">The name of the parameter</param>
    /// <param name="value">The optional value of the parameter</param>
    /// <returns>The command with the parameter added</returns>
    public static T AddNamedParameter<T>(this T self, string name, object? value = null)
        where T : IDbCommand
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
            ((IDataParameter)self.Parameters[kvp.Key]!).Value = kvp.Value;
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
        ((IDataParameter)self.Parameters[name]!).Value = value;
        return self;
    }

    /// <summary>
    /// Sets the transaction for the command.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="self">The command to set the transaction for</param>
    /// <param name="transaction">The transaction to set for the command</param>
    /// <returns>>The command with the transaction set</returns>
    public static T SetTransaction<T>(this T self, IDbTransaction? transaction)
        where T : IDbCommand
    {
        self.Transaction = transaction;
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

        foreach (var p in self.Parameters.Cast<IDbDataParameter>())
        {
            var ix = txt.IndexOf('?');
            if (ix >= 0)
            {
                string v;
                if (p.Value is string)
                    v = string.Format("\"{0}\"", p.Value);
                else if (p.Value == null)
                    v = "NULL";
                else
                    v = string.Format("{0}", p.Value);

                txt = txt.Substring(0, ix) + v + txt.Substring(ix + 1);
            }
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
    {
        return ExecuteNonQuery(self, true, cmd);
    }

    /// <summary>
    /// Executes the command and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>The number of rows affected</returns>
    public static int ExecuteNonQuery(this IDbCommand self, string cmd, Dictionary<string, object?> values)
    {
        return ExecuteNonQuery(self, true, cmd, values);
    }

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
    {
        return ExecuteScalar(self, true, cmd);
    }

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
    {
        return ExecuteScalarInt64(self, writeLog, null, defaultvalue);
    }

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this IDbCommand self, long defaultvalue = -1)
    {
        return ExecuteScalarInt64(self, true, null, defaultvalue);
    }

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
    {
        return ExecuteScalarInt64(self, true, cmd, defaultvalue);
    }

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
    {
        return ExecuteReader(self, true, cmd, values);
    }


    /// <summary>
    /// Executes the command and returns a data reader.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <returns>A <see cref="IDataReader"/> instance</returns>
    public static IDataReader ExecuteReader(this IDbCommand self, string cmd)
    {
        return ExecuteReader(self, true, cmd);
    }

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
    /// Creates the transaction for the given connection and executes a "BEGIN IMMEDIATE" command to ensure that the transaction can be comitted or rolled back.
    /// This works around a quirk in SQLite where the transaction is not initialized until the first command is executed.
    /// This means that if the transaction is rolled back or committed with no commands executed, it will fail with an exception.
    /// </summary>
    /// <param name="self">The connection to create the transaction on</param>
    /// <returns>The transaction</returns>
    public static IDbTransaction BeginTransactionSafe(this IDbConnection self)
    {
        var transaction = self.BeginTransaction();
        // using (var cmd = self.CreateCommand(transaction, "BEGIN IMMEDIATE;"))
        //     try { cmd.ExecuteNonQuery(); }
        //     catch { /* ignore */ }

        return transaction;
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
    public static TCommand ExpandInClauseParameter<TValue, TCommand>(this TCommand cmd, string originalParamName, IEnumerable<TValue> values)
        where TCommand : IDbCommand
    {
        if (string.IsNullOrWhiteSpace(originalParamName) || !originalParamName.StartsWith("@"))
            throw new ArgumentException("Parameter name must start with '@'", nameof(originalParamName));

        foreach (var p in cmd.Parameters)
            if (p is IDataParameter parameter && parameter.ParameterName.Equals(originalParamName, StringComparison.OrdinalIgnoreCase))
            {
                cmd.Parameters.Remove(parameter);
                break;
            }

        foreach ((var value, var index) in values.Select((value, index) => (value, index)))
            cmd.AddNamedParameter($"{originalParamName}{index}", value);

        var inClause = string.Join(", ", values.Select((_, index) => $"{originalParamName}{index}"));
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
    public static T ExpandInClauseParameter<T>(this T cmd, string originalParamName, TemporaryDbValueList values)
        where T : IDbCommand
    {
        if (string.IsNullOrWhiteSpace(originalParamName) || !originalParamName.StartsWith("@"))
            throw new ArgumentException("Parameter name must start with '@'", nameof(originalParamName));

        if (!values.IsTableCreated)
            return ExpandInClauseParameter(cmd, originalParamName, values.Values);

        // We have a temporary table, so we need to replace the parameter with the table name
        cmd.CommandText = cmd.CommandText.Replace(originalParamName, values.GetInClause(), StringComparison.OrdinalIgnoreCase);
        return cmd;
    }
}