#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
    /// This is the signature for the log event
    /// </summary>
    /// <param name="message">The message logged</param>
    /// <param name="type">The type of message logged</param>
    /// <param name="ex">An exception, may be null</param>
    public delegate void EventLoggedDelgate(string message, LogMessageType type, Exception ex);

    /// <summary>
    /// This static class is used to write log messages
    /// </summary>
    public static class Log
    {
        /// <summary>
        /// Static lock object to provide thread safe logging
        /// </summary>
        private static object m_lock = new object();
        /// <summary>
        /// The log destination, may be null
        /// </summary>
        private static ILog m_log = null;
        /// <summary>
        /// The minimum level of logged messages
        /// </summary>
        private static LogMessageType m_logLevel = LogMessageType.Error;

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
            if (m_log == null)
                return;

            lock (m_lock)
                if (m_log == null)
                    return;
                else
                {
                    if (type >= m_logLevel)
                        m_log.WriteMessage(message, type, ex);
                }
        }

        /// <summary>
        /// Gets or sets the level of log messages to write to the CurrentLog
        /// </summary>
        public static LogMessageType LogLevel
        {
            get { return m_logLevel; }
            set
            {
                lock (m_lock)
                    m_logLevel = value;
            }
        }

        /// <summary>
        /// Gets or sets the current log destination
        /// </summary>
        public static ILog CurrentLog
        {
            get 
            {
                return m_log;
            }
            set 
            {
                lock (m_lock)
                    m_log = value;
            }
        }
    }
}
