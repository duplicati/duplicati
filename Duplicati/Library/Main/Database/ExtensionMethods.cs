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
using System.Linq;

#nullable enable

namespace Duplicati.Library.Main.Database;

/// <summary>
/// Extension method for <see cref="System.Data.IDbCommand"/>
/// </summary>
public static class ExtensionMethods
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ExtensionMethods));

    /// <summary>
    /// Adds and sets the parameters for the given command.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="cmd">The command to add and set the parameters for</param>
    /// <param name="parameters">The parameters to add and set</param>
    /// <returns>The command with the parameters added and set</returns>
    public static T AddAndSetParameters<T>(this T cmd, params object?[]? parameters)
        where T : System.Data.IDbCommand
    {
        if (parameters != null)
            for (var i = 0; i < parameters.Length; i++)
            {
                var p = cmd.CreateParameter();
                p.Value = parameters[i];
                cmd.Parameters.Add(p);
            }

        return cmd;
    }

    /// <summary>
    /// Adds the given number of parameters to the command.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="self">The command to add the parameters to</param>
    /// <param name="count">The number of parameters to add</param>
    /// <returns>The command with the parameters added</returns>
    public static T AddParameters<T>(this T self, int count)
        where T : System.Data.IDbCommand
    {
        for (var i = 0; i < count; i++)
            self.Parameters.Add(self.CreateParameter());

        return self;
    }

    /// <summary>
    /// Adds a parameter to the command.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="self">The command to add the parameter to</param>
    /// <returns>The command with the parameter added</returns>
    public static T AddParameter<T>(this T self)
        where T : System.Data.IDbCommand
    {
        self.Parameters.Add(self.CreateParameter());
        return self;
    }

    /// <summary>
    /// Adds a parameter to the command with the given value.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="self">The command to add the parameter to</param>
    /// <param name="value">The value of the parameter</param>
    /// <param name="name">The optional name of the parameter</param>
    /// <returns>The command with the parameter added</returns>
    public static T AddParameter<T>(this T self, object? value, string? name = null)
        where T : System.Data.IDbCommand
    {
        var p = self.CreateParameter();
        p.Value = value;
        if (!string.IsNullOrEmpty(name))
            p.ParameterName = name;
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
    public static T SetParameterValues<T>(this T self, params object?[] values)
        where T : System.Data.IDbCommand
    {
        for (var i = 0; i < values.Length; i++)
            ((System.Data.IDataParameter)self.Parameters[i]!).Value = values[i];
        return self;
    }

    /// <summary>
    /// Sets the parameter value for the command at the given index.
    /// </summary>
    /// <typeparam name="T">The type of the command</typeparam>
    /// <param name="self">The command to set the parameter value for</param>
    /// <param name="index">The index of the parameter to set the value for</param>
    /// <param name="value">The value to set the parameter to</param>
    /// <returns>The command with the parameter value set</returns>
    public static T SetParameterValue<T>(this T self, int index, object? value)
        where T : System.Data.IDbCommand
    {
        ((System.Data.IDataParameter)self.Parameters[index]!).Value = value;
        return self;
    }

    /// <summary>
    /// Gets the printable command text for the given command.
    /// </summary
    /// <param name="self">The command to get the printable command text for</param>
    /// <returns>The printable command text</returns>
    public static string GetPrintableCommandText(this System.Data.IDbCommand self)
    {
        var txt = self.CommandText;

        foreach (var p in self.Parameters.Cast<System.Data.IDbDataParameter>())
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
    public static int ExecuteNonQuery(this System.Data.IDbCommand self, bool writeLog)
    {
        return ExecuteNonQuery(self, writeLog, null, null);
    }

    /// <summary>
    /// Executes the command and returns the number of rows affected.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>The number of rows affected</returns>
    public static int ExecuteNonQuery(this System.Data.IDbCommand self, string cmd, params object?[]? values)
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
    public static int ExecuteNonQuery(this System.Data.IDbCommand self, bool writeLog, string? cmd, params object?[]? values)
    {
        if (cmd != null)
            self.CommandText = cmd;

        if (values != null && values.Length > 0)
        {
            self.Parameters.Clear();
            foreach (var n in values)
                self.AddParameter(n);
        }

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteNonQuery", string.Format("ExecuteNonQuery: {0}", self.GetPrintableCommandText())) : null)
            return self.ExecuteNonQuery();
    }

    /// <summary>
    /// Executes the command and returns the scalar value of the first row.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>The scalar value of the first row</returns>
    public static object? ExecuteScalar(this System.Data.IDbCommand self, string cmd, params object[] values)
    {
        return ExecuteScalar(self, true, cmd, values);
    }

    /// <summary>
    /// Executes the command and returns the scalar value of the first row.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>The scalar value of the first row</returns>
    public static object? ExecuteScalar(this System.Data.IDbCommand self, bool writeLog, string cmd, params object[] values)
    {
        if (cmd != null)
            self.CommandText = cmd;

        if (values != null && values.Length > 0)
        {
            self.Parameters.Clear();
            foreach (var n in values)
                self.AddParameter(n);
        }

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
    public static long ExecuteScalarInt64(this System.Data.IDbCommand self, bool writeLog, long defaultvalue = -1)
    {
        return ExecuteScalarInt64(self, writeLog, null, defaultvalue);
    }

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this System.Data.IDbCommand self, long defaultvalue = -1)
    {
        return ExecuteScalarInt64(self, true, null, defaultvalue);
    }

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this System.Data.IDbCommand self, bool writeLog, string? cmd, long defaultvalue = -1)
    {
        return ExecuteScalarInt64(self, writeLog, cmd, defaultvalue, null);
    }

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this System.Data.IDbCommand self, string? cmd, long defaultvalue = -1)
    {
        return ExecuteScalarInt64(self, true, cmd, defaultvalue, null);
    }

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this System.Data.IDbCommand self, string? cmd, long defaultvalue, params object?[]? values)
    {
        return ExecuteScalarInt64(self, true, cmd, defaultvalue, values);
    }

    /// <summary>
    /// Executes the command and returns the scalar int64 value of the first row as a string.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="defaultvalue">The default value to return if no value is found</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>The scalar int64 value of the first row as a string</returns>
    public static long ExecuteScalarInt64(this System.Data.IDbCommand self, bool writeLog, string? cmd, long defaultvalue, params object?[]? values)
    {
        if (cmd != null)
            self.CommandText = cmd;

        if (values != null && values.Length > 0)
        {
            self.Parameters.Clear();
            foreach (var n in values)
                self.AddParameter(n);
        }

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
    /// <returns>A <see cref="System.Data.IDataReader"/> instance</returns>
    public static System.Data.IDataReader ExecuteReader(this System.Data.IDbCommand self, string cmd, params object[] values)
    {
        return ExecuteReader(self, true, cmd, values);
    }

    /// <summary>
    /// Executes the command and returns a data reader.
    /// </summary>
    /// <param name="self">The command instance to execute on</param>
    /// <param name="writeLog">Whether to write a log entry</param>
    /// <param name="cmd">The command string to execute</param>
    /// <param name="values">The values to use as parameters. The parameters must already be added.</param>
    /// <returns>A <see cref="System.Data.IDataReader"/> instance</returns>
    public static System.Data.IDataReader ExecuteReader(this System.Data.IDbCommand self, bool writeLog, string cmd, params object[] values)
    {
        if (cmd != null)
            self.CommandText = cmd;

        if (values != null && values.Length > 0)
        {
            self.Parameters.Clear();
            foreach (var n in values)
                self.AddParameter(n);
        }

        using (writeLog ? new Logging.Timer(LOGTAG, "ExecuteReader", string.Format("ExecuteReader: {0}", self.GetPrintableCommandText())) : null)
            return self.ExecuteReader();
    }

    /// <summary>
    /// Executes the command and returns an enumerable of data readers.
    /// </summary>
    /// <param name="self">The database command to execute.</param>
    /// <returns>An enumerable of data readers.</returns>
    public static IEnumerable<System.Data.IDataReader> ExecuteReaderEnumerable<T>(this System.Data.IDbCommand self)
    {
        using var rd = self.ExecuteReader();
        while (rd.Read())
            yield return rd;
    }

    /// <summary>
    /// Executes the given command string `cmd` on the given database command `self` with the given values `values` and returns an enumerable of data readers.
    /// </summary>
    /// <param name="self">The database command to execute on.</param>
    /// <param name="cmd">The command string to execute.</param>
    /// <param name="values">The values that the command string should be parameterized with.</param>
    /// <returns></returns>
    public static IEnumerable<System.Data.IDataReader> ExecuteReaderEnumerable(this System.Data.IDbCommand self, string cmd, params object[] values)
    {
        using var rd = ExecuteReader(self, cmd, values);
        while (rd.Read())
            yield return rd;
    }

    /// <summary>
    /// Converts the value at the given index of the given data reader to a string.
    /// </summary>
    /// <param name="reader">The data reader to convert the value from.</param>
    /// <param name="index">The index of the value to convert.</param>
    /// <returns>The value at the given index as a string.</returns>
    public static string? ConvertValueToString(this System.Data.IDataReader reader, int index)
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
    public static long ConvertValueToInt64(this System.Data.IDataReader reader, int index, long defaultvalue = -1)
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
    /// <returns>A new command with the given transaction.</returns>
    public static System.Data.IDbCommand CreateCommand(this System.Data.IDbConnection self, System.Data.IDbTransaction transaction)
    {
        var con = self.CreateCommand();
        con.Transaction = transaction;
        return con;
    }
}

