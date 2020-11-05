#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Logging
{
    /// <summary>
    /// The different types of messages
    /// </summary>
    public enum LogMessageType
    {
        /// <summary>
        /// The message should only be shown if it is explicitly requested
        /// </summary>
        ExplicitOnly,
        /// <summary>
        /// The message is a profiling message
        /// </summary>
        Profiling,
        /// <summary>
        /// Messages that are normally not wanted for display
        /// </summary>
        Verbose,
        /// <summary>
        /// The message is from a retry
        /// </summary>
        Retry,
        /// <summary>
        /// The message is informative but does not indicate problems
        /// </summary>
        Information,
        /// <summary>
        /// The message is from dry-run output
        /// </summary>
        DryRun,
        /// <summary>
        /// The message is a warning, meaning that later errors may be related to this message
        /// </summary>
        Warning,
        /// <summary>
        /// The message indicates an error
        /// </summary>
        Error,
    }

    /// <summary>
    /// This static class is used to write log messages
    /// </summary>
    public static class Log
    {
        /// <summary>
        /// The key used to assign the current scope into the current call-context
        /// </summary>
        private const string LOGICAL_CONTEXT_KEY = "Duplicati:LoggingEntry";

        /// <summary>
        /// The root scope
        /// </summary>
        private static readonly LogScope m_root = new LogScope(null, new LogTagFilter(LogMessageType.Error, null, null), null, true);

        /// <summary>
        /// The stored log instances
        /// </summary>
        private static readonly Dictionary<string, LogScope> m_log_instances = new Dictionary<string, LogScope>();

        /// <summary>
        /// Static lock object to provide thread safe logging
        /// </summary>
        private static readonly object m_lock = new object();

        /// <summary>
        /// Gets the lock instance used to protect the logging calls
        /// </summary>
        public static object Lock { get { return m_lock; } }

        /// <summary>
        /// Gets a log tag that reflects the type
        /// </summary>
        /// <returns>The log-tag for the type.</returns>
        /// <typeparam name="T">The type to get the tag for.</typeparam>
        public static string LogTagFromType<T>()
        {
            return LogTagFromType(typeof(T));
        }


        /// <summary>
        /// Gets a log tag that reflects the type
        /// </summary>
        /// <returns>The log-tag for the type.</returns>
        /// <param name="t">The type to get the tag for.</param>
        public static string LogTagFromType(Type t)
        {
            return t.Namespace + "." + t.Name;
        }

        /// <summary>
        /// Gets a common list of items to include in the help-url
        /// </summary>
        /// <returns>The basic help url items.</returns>
        /// <param name="fromCommandLine">A flag indicating if the link is for commandline</param>
        private static List<string> GetBasicHelpUrlItems(bool fromCommandLine)
        {
            var items = new List<string>();
            items.Add("version=" + System.Uri.EscapeDataString(typeof(Log).Assembly.GetName().Version.ToString()));
            items.Add("cli=" + (fromCommandLine ? "t" : "f"));
            // TODO: Add OS type? mono version? install id? app-name?

            return items;
        }

        /// <summary>
        /// Encodes a list of query string values and appends them to the help url
        /// </summary>
        /// <returns>The help URL.</returns>
        /// <param name="items">The items to include.</param>
        private static string EncodeHelpUrl(IEnumerable<string> items)
        {
            var query = string.Join("&", items);
            if (!string.IsNullOrWhiteSpace(query))
                query = "?" + query;

            return "https://help.duplicati.com/" + query;
        }

        /// <summary>
        /// Creates a link to look up further information on a particular message
        /// </summary>
        /// <returns>The help link.</returns>
        /// <param name="id">The id to use.</param>
        /// <param name="fromCommandLine">A flag indicating if the link is for commandline</param>
        public static string CreateHelpLink(string id, bool fromCommandLine)
        {
            var items = GetBasicHelpUrlItems(fromCommandLine);
            if (!string.IsNullOrWhiteSpace(id))
                items.Add("id=" + System.Uri.EscapeDataString(id));
            return EncodeHelpUrl(items);
        }

        /// <summary>
        /// Creates a link to look up further information on a particular message
        /// </summary>
        /// <returns>The help link.</returns>
        /// <param name="tag">The log tag, if any.</param>
        /// <param name="messageid">The message id, if any.</param>
        /// <param name="exceptionid">The exception id, if any.</param>
        /// <param name="fromCommandLine">A flag indicating if the link is for commandline</param>
        public static string CreateHelpLink(string tag, string messageid, string exceptionid, bool fromCommandLine)
        {
            var items = GetBasicHelpUrlItems(fromCommandLine);
            if (!string.IsNullOrWhiteSpace(tag))
                items.Add("tag=" + System.Uri.EscapeDataString(tag));
            if (!string.IsNullOrWhiteSpace(messageid))
                items.Add("msgid=" + System.Uri.EscapeDataString(messageid));
            if (!string.IsNullOrWhiteSpace(exceptionid))
                items.Add("exid=" + System.Uri.EscapeDataString(exceptionid));

            return EncodeHelpUrl(items);
        }

        /// <summary>
        /// Writes an explicit message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="arguments">The message format arguments</param>
        public static void WriteExplicitMessage(string tag, string id, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.ExplicitOnly, tag, id, null, message, arguments);
        }

        /// <summary>
        /// Writes an explicit message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="ex">The exception to log</param>
        /// <param name="arguments">The message format arguments</param>
        public static void WriteExplicitMessage(string tag, string id, Exception ex, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.ExplicitOnly, tag, id, ex, message, arguments);
        }

        /// <summary>
        /// Writes a verbose message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="arguments">The message format arguments</param>
        public static void WriteVerboseMessage(string tag, string id, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.Verbose, tag, id, null, message, arguments);
        }

		/// <summary>
        /// Writes a verbose message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
		/// <param name="ex">The exception to log</param>
        /// <param name="arguments">The message format arguments</param>
		public static void WriteVerboseMessage(string tag, string id, Exception ex, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.Verbose, tag, id, ex, message, arguments);
        }

        /// <summary>
        /// Writes a profiling message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="arguments">The message format arguments</param>
        public static void WriteProfilingMessage(string tag, string id, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.Profiling, tag, id, null, message, arguments);
        }

        /// <summary>
        /// Writes a dry-run message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="arguments">The message format arguments</param>
        public static void WriteDryrunMessage(string tag, string id, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.DryRun, tag, id, null, message, arguments);
        }

        /// <summary>
        /// Writes a retry message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="ex">The exception to attach</param>
        /// <param name="arguments">The message format arguments</param>
        public static void WriteRetryMessage(string tag, string id, Exception ex, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.Retry, tag, id, ex, message, arguments);
        }

        /// <summary>
        /// Writes an information message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="arguments">The message format arguments</param>
        public static void WriteInformationMessage(string tag, string id, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.Information, tag, id, null, message, arguments);
        }

        /// <summary>
        /// Writes a warning message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="ex">The exception to attach</param>
        /// <param name="arguments">The message format arguments</param>
        public static void WriteWarningMessage(string tag, string id, Exception ex, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.Warning, tag, id, ex, message, arguments);
        }

        /// <summary>
        /// Writes an error message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="ex">The exception to attach</param>
        /// <param name="arguments">The message format arguments</param>
        public static void WriteErrorMessage(string tag, string id, Exception ex, string message, params object[] arguments)
        {
            WriteMessage(LogMessageType.Error, tag, id, ex, message, arguments);
        }

        /// <summary>
        /// Writes a message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="type">The type of the message</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        public static void WriteMessage(LogMessageType type, string tag, string id, string message, params object[] arguments)
        {
            WriteMessage(type, tag, id, null, message, arguments);
        }

        /// <summary>
        /// Writes a message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="type">The type of the message</param>
        /// <param name="ex">An exception value</param>
        /// <param name="tag">The tag-type for this message</param>
        /// <param name="id">The message id</param>
        /// <param name="arguments">The arguments to format the log message with</param>
        public static void WriteMessage(LogMessageType type, string tag, string id, Exception ex, string message, params object[] arguments)
        {
            var msg = new LogEntry(message, arguments, type, tag, id, ex);
            Console.WriteLine(message, arguments);
            lock (m_lock)
            {
                var cs = CurrentScope;
                while (cs != null && !cs.IsolatingScope)
                {
                    cs.WriteMessage(msg);
                    cs = cs.Parent;
                }
            }
        }

        /// <summary>
        /// Starts a new scope, that can be closed by disposing the returned instance
        /// </summary>
        /// <param name="detached">Flag indicating if the scope should be detached from the parent</param>
        /// <returns>The new scope.</returns>
        public static IDisposable StartIsolatingScope(bool detached)
        {
            lock (m_lock)
            {
                var scope = StartScope(null, null, true);
                if (detached)
                    DetachCurrentScope(scope);
                return scope;
            }
        }

        /// <summary>
        /// Detaches the current scope, such that new scopes do not chain onto this
        /// </summary>
        /// <param name="scope">The current scope.</param>
        public static IDisposable DetachCurrentScope(IDisposable scope)
        {
            lock (m_lock)
            {
                if (CurrentScope == scope && scope != null && CurrentScope.Parent != null)
                    CurrentScope = CurrentScope.Parent;
            }

            return scope;
        }

        /// <summary>
        /// Starts a new scope, that can be closed by disposing the returned instance
        /// </summary>
        /// <returns>The new scope.</returns>
        public static IDisposable StartScope()
        {
            return StartScope((ILogDestination)null, null);
        }

        /// <summary>
        /// Starts a new scope, that can be stopped by disposing the returned instance
        /// </summary>
        /// <param name="log">The log target</param>
        /// <param name="level">The log level</param>
        /// <returns>The new scope.</returns>
        public static IDisposable StartScope(ILogDestination log, LogMessageType level)
        {
            return StartScope(log, new LogTagFilter(level, null, null));
        }

        /// <summary>
        /// Starts a new scope, that can be stopped by disposing the returned instance
        /// </summary>
        /// <param name="log">The log target</param>
        /// <param name="filter">The log filter</param>
        /// <returns>The new scope.</returns>
        public static IDisposable StartScope(ILogDestination log, ILogFilter filter = null, bool isolating = false)
        {
            return new LogScope(log, filter, CurrentScope, isolating);
        }

        /// <summary>
        /// Starts a new scope, that can be stopped by disposing the returned instance
        /// </summary>
        /// <param name="log">The log target</param>
        /// <param name="filter">The log filter</param>
        /// <returns>The new scope.</returns>
        public static IDisposable StartScope(Action<LogEntry> log, Func<LogEntry, bool> filter = null)
        {
            return new LogScope(new FunctionLogDestination(log), filter == null ? null : new FunctionFilter(filter), CurrentScope, false);
        }

        /// <summary>
        /// Starts the scope.
        /// </summary>
        /// <param name="scope">The scope to start.</param>
        internal static void StartScope(LogScope scope)
        {
            CurrentScope = scope;
        }

        /// <summary>
        /// Closes the scope.
        /// </summary>
        /// <param name="scope">The scope to finish.</param>
        internal static void CloseScope(LogScope scope)
        {
            lock(m_lock)
            {
                if (CurrentScope == scope && scope != m_root)
                    CurrentScope = scope.Parent;
                m_log_instances.Remove(scope.InstanceID);
            }
        }

        /// <summary>
        /// Gets or sets the current log destination in a call-context aware fashion
        /// </summary>
        internal static LogScope CurrentScope
        {
            get
            {
                lock (m_lock)
                {
                    var cur = System.Runtime.Remoting.Messaging.CallContext.LogicalGetData(LOGICAL_CONTEXT_KEY) as string;
                    if (cur == null || cur == m_root.InstanceID)
                        return m_root;
                    
                    LogScope sc;
                    if (!m_log_instances.TryGetValue(cur, out sc))
                        throw new Exception("Unable to find log in lookup table, this may be caused by attempting to transport call contexts between AppDomains (eg. with remoting calls)");

                    return sc;
                }
            }
            private set
            {
                lock (m_lock)
                {
                    if (value != null)
                    {
                        m_log_instances[value.InstanceID] = value;
                        System.Runtime.Remoting.Messaging.CallContext.LogicalSetData(LOGICAL_CONTEXT_KEY, value.InstanceID);
                    }
                    else
                    {
                        System.Runtime.Remoting.Messaging.CallContext.LogicalSetData(LOGICAL_CONTEXT_KEY, null);
                    }
                }
            }
        }
    }
}
