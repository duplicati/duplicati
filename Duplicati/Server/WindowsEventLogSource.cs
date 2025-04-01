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
using System.Diagnostics;
using System.Runtime.Versioning;
using Duplicati.Library.Logging;

namespace Duplicati.Server
{
    /// <summary>
    /// Writes log messages to the Windows Event Log
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsEventLogSource : ILogDestination, IDisposable
    {
        /// <summary>
        /// The log tag for the windows events
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType<WindowsEventLogSource>();

        /// <summary>
        /// The event log to write to
        /// </summary>
        private readonly EventLog m_eventLog;

        /// <summary>
        /// Initializes a new instance of the <see cref="WindowsEventLogSource"/> class.
        /// </summary>
        /// <param name="source">The source of the log messages</param>
        /// <param name="log">The log to write to</param>
        public WindowsEventLogSource(string source, string? log = null)
        {
            (source, log) = SplitSource(source, log);
            m_eventLog = new EventLog
            {
                Source = source,
                Log = log
            };
        }

        /// <summary>
        /// Checks if the source exists
        /// </summary>
        /// <param name="source">The source to check</param>
        /// <returns>True if the source exists</returns>
        public static bool SourceExists(string source)
        {
            try
            {
                (source, _) = SplitSource(source, null);
                return EventLog.SourceExists(source);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "EventLogLookupFailure", ex, $"Failed to test if event log {source} exists");
            }

            return false;
        }

        /// <summary>
        /// Creates a new event source
        /// </summary>
        /// <param name="source">The source to create</param>
        /// <param name="log">The log to write to</param>
        public static void CreateEventSource(string source, string? log = null)
        {
            (source, log) = SplitSource(source, log);
            if (!SourceExists(source))
                EventLog.CreateEventSource(source, log);
        }

        /// <inheritdoc />
        public void Dispose() => m_eventLog.Dispose();

        /// <inheritdoc />
        public void WriteMessage(LogEntry entry)
            => m_eventLog.WriteEntry(entry.AsString(true), ToEventLogType(entry.Level));

        /// <summary>
        /// Parse a log and source name from a string, using a colon as a separator.
        /// If no colon is found, the source is assumed to be "Application".
        /// </summary>
        /// <param name="source">The source name to parse</param>
        /// <returns></returns>
        private static (string Log, string Source) SplitSource(string source, string? log = null)
        {
            if (source.Contains(':') && string.IsNullOrWhiteSpace(log))
            {
                var parts = source.Split(':', 2);
                if (!string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                    return (parts[1], parts[0]);
            }

            if (string.IsNullOrWhiteSpace(log))
                log = "Duplicati 2";

            return (source, log);
        }

        /// <summary>
        /// Converts a log message type to an windows event log type
        /// </summary>
        /// <param name="level">The log message type</param>
        /// <returns>The windows event log type</returns>
        private static EventLogEntryType ToEventLogType(LogMessageType level)
        {
            return level switch
            {
                LogMessageType.ExplicitOnly => EventLogEntryType.Information,
                LogMessageType.Profiling => EventLogEntryType.Information,
                LogMessageType.Verbose => EventLogEntryType.Information,
                LogMessageType.Retry => EventLogEntryType.Warning,
                LogMessageType.Information => EventLogEntryType.Information,
                LogMessageType.DryRun => EventLogEntryType.Information,
                LogMessageType.Warning => EventLogEntryType.Warning,
                LogMessageType.Error => EventLogEntryType.Error,
                _ => EventLogEntryType.Information
            };
        }
    }
}