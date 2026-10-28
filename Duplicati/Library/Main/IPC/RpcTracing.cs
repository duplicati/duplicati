// Copyright (C) 2026, The Duplicati Team
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
using System.Diagnostics;
using System.IO;
using StreamJsonRpc;

#nullable enable

namespace Duplicati.Library.Main.IPC;

/// <summary>
/// Opt-in tracing of the JSON-RPC traffic between the server and the controller process.
/// Enabled by setting the <see cref="TRACE_ENV_VAR"/> environment variable, which the
/// controller process inherits from the server, so one setting covers both sides.
/// </summary>
public static class RpcTracing
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType(typeof(RpcTracing));

    /// <summary>
    /// The environment variable that enables RPC tracing.
    /// In the controller process any non-empty value traces to stderr, which the server
    /// forwards to its log. In the server a rooted file path appends the trace to that
    /// file; any other non-empty value writes the trace to the Duplicati log at verbose level.
    /// </summary>
    public const string TRACE_ENV_VAR = "DUPLICATI_CONTROLLER_RPC_TRACE";

    /// <summary>
    /// Returns <c>true</c> if RPC tracing is requested
    /// </summary>
    public static bool IsEnabled => IsTruthy(Environment.GetEnvironmentVariable(TRACE_ENV_VAR));

    /// <summary>
    /// Checks if the string value is true.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><c>true</c> if the value represents a truthy value; otherwise, <c>false</c>.</returns>
    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.Equals("1") || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Traces the RPC traffic of the controller process to stderr, if enabled.
    /// Stdout must stay untouched, as it carries the RPC stream itself.
    /// </summary>
    /// <param name="rpc">The RPC connection to trace</param>
    /// <returns>The attached listener, or <c>null</c> if tracing is disabled</returns>
    public static TraceListener? AttachStderrListener(JsonRpc rpc)
    {
        if (!IsEnabled)
            return null;

        var listener = new TextWriterTraceListener(Console.Error, "controller-rpc");
        Attach(rpc, listener);
        return listener;
    }

    /// <summary>
    /// Traces the RPC traffic of the server side of the connection, if enabled.
    /// </summary>
    /// <param name="rpc">The RPC connection to trace</param>
    /// <param name="description">A description of the peer, written as a header line</param>
    /// <returns>The attached listener, or <c>null</c> if tracing is disabled</returns>
    public static TraceListener? AttachServerListener(JsonRpc rpc, string description)
    {
        var value = Environment.GetEnvironmentVariable(TRACE_ENV_VAR);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        TraceListener listener;
        if (Path.IsPathRooted(value))
        {
            try
            {
                // Several controller processes can run at once, so the file is opened for shared append
                var stream = new FileStream(value, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                listener = new TextWriterTraceListener(new StreamWriter(stream) { AutoFlush = true }, "controller-rpc");
            }
            catch (Exception ex)
            {
                Library.Logging.Log.WriteWarningMessage(LOGTAG, "RpcTraceFileError", ex, "Failed to open the RPC trace file {0}, tracing to the log instead", value);
                listener = new LogTraceListener();
            }
        }
        else
        {
            listener = new LogTraceListener();
        }

        listener.WriteLine($"[{DateTime.Now:O}] RPC trace started for {description}");
        Attach(rpc, listener);
        return listener;
    }

    /// <summary>
    /// Attaches the listener to the RPC connection and raises the trace level so all traffic is recorded
    /// </summary>
    private static void Attach(JsonRpc rpc, TraceListener listener)
    {
        rpc.TraceSource.Switch.Level = SourceLevels.Verbose;
        rpc.TraceSource.Listeners.Add(listener);
    }

    /// <summary>
    /// Trace listener that forwards each line to the Duplicati log as a verbose message
    /// </summary>
    private sealed class LogTraceListener : TraceListener
    {
        /// <inheritdoc />
        public override void Write(string? message)
            => WriteLine(message);

        /// <inheritdoc />
        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Library.Logging.Log.WriteVerboseMessage(LOGTAG, "RpcTrace", "{0}", message);
        }
    }
}
