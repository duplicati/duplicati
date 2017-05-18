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
        /// The message is a profiling message
        /// </summary>
        Profiling,
        /// <summary>
        /// The message is informative but does not indicate problems
        /// </summary>
        Information,
        /// <summary>
        /// The message is a warning, meaning that later errors may be releated to this message
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
        private static readonly LogScope m_root = new LogScope(null, LogMessageType.Error, null);

        /// <summary>
        /// The stored log instances
        /// </summary>
        private static readonly Dictionary<string, LogScope> m_log_instances = new Dictionary<string, LogScope>();

        /// <summary>
        /// Static lock object to provide thread safe logging
        /// </summary>
        private static object m_lock = new object();

        /// <summary>
        /// Flag used to block recursion
        /// </summary>
        private static bool m_is_active = false;

        /// <summary>
        /// Gets the lock instance used to protect the logging calls
        /// </summary>
        public static object Lock { get { return m_lock; } }

        /// <summary>
        /// Writes a message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="type">The type of the message</param>
        public static void WriteMessage(string message, LogMessageType type)
        {
            WriteMessage(message, type, null);
        }

        /// <summary>
        /// Writes a message to the current log destination
        /// </summary>
        /// <param name="message">The message to write</param>
        /// <param name="type">The type of the message</param>
        /// <param name="ex">An exception value</param>
        public static void WriteMessage(string message, LogMessageType type, Exception ex)
        {
            lock (m_lock)
            {
                if (m_is_active)
                    return;

                try
                {
                    m_is_active = true;
                    var cs = CurrentScope;
                    while (cs != null)
                    {
                        if (cs.Log != null && type >= cs.LogLevel)
                            cs.Log.WriteMessage(message, type, ex);
                        cs = cs.Parent;
                    }
                }
                finally
                {
                    m_is_active = false;
                }
            }
        }

        /// <summary>
        /// Gets or sets the level of log messages to write to the CurrentLog
        /// </summary>
        public static LogMessageType LogLevel
        {
            get 
            {
                var cs = CurrentScope;
                var ll = (int)cs.LogLevel;
                while (cs != null)
                {
                    ll = Math.Min(ll, (int)cs.LogLevel);
                    cs = cs.Parent;
                }
                return (LogMessageType)ll;
            }
            set
            {
                CurrentScope.LogLevel = value;
            }
        }

        /// <summary>
        /// Gets or sets the current log destination
        /// </summary>
        public static ILog CurrentLog
        {
            get 
            {
                return CurrentScope.Log;
            }
            set 
            {
                CurrentScope.Log = value;
            }
        }

        /// <summary>
        /// Starts a new scope, that can be closed by disposing the returned instance
        /// </summary>
        /// <returns>The new scope.</returns>
        public static IDisposable StartScope()
        {
            return StartScope((ILog)null);
        }

        /// <summary>
        /// Starts a new scope, that can be stopped by disposing the returned instance
        /// </summary>
        /// <returns>The new scope.</returns>
        public static IDisposable StartScope(ILog log)
        {
            return new LogScope(log, LogLevel, CurrentScope);
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
                        throw new Exception(string.Format("Unable to find log in lookup table, this may be caused by attempting to transport call contexts between AppDomains (eg. with remoting calls)"));

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
