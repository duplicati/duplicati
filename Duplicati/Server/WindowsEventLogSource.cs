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
        /// The event log to write to
        /// </summary>
        private readonly EventLog m_eventLog;

        /// <summary>
        /// Initializes a new instance of the <see cref="WindowsEventLogSource"/> class.
        /// </summary>
        /// <param name="source">The source of the log messages</param>
        /// <param name="log">The log to write to</param>
        public WindowsEventLogSource(string source, string log = "Application")
        {
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
            => EventLog.SourceExists(source);

        /// <inheritdoc />
        public void Dispose() => m_eventLog.Dispose();

        /// <inheritdoc />
        public void WriteMessage(LogEntry entry)
            => m_eventLog.WriteEntry(entry.AsString(true), ToEventLogType(entry.Level));

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