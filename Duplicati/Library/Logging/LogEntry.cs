//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.Library.Logging
{
    /// <summary>
    /// Instance of a log entry
    /// </summary>
    public class LogEntry
    {
        /// <summary>
        /// The time when the entry was logged
        /// </summary>
        public DateTime When;

        /// <summary>
        /// The message that was logged
        /// </summary>
        public readonly string Message;

        /// <summary>
        /// The string format arguments, if any
        /// </summary>
        public readonly object[] Arguments;

        /// <summary>
        /// The message level
        /// </summary>
        public LogMessageType Level;

        /// <summary>
        /// The filter tag for the message
        /// </summary>
        public readonly string FilterTag;

        /// <summary>
        /// The filter tag for the message
        /// </summary>
        public readonly string Tag;

        /// <summary>
        /// The message help-id
        /// </summary>
        public readonly string Id;

        /// <summary>
        /// An optional exception
        /// </summary>
        public readonly Exception Exception;

        /// <summary>
        /// The log context data, if any
        /// </summary>
        private Dictionary<string, string> m_logContext;

        /// <summary>
        /// Gets or sets the <see cref="T:Duplicati.Library.Logging.LogEntry"/> with the specified value.
        /// </summary>
        /// <param name="value">The key to use.</param>
        public string this[string key]
        {
            get
            {
                if (m_logContext == null)
                    return null;
                m_logContext.TryGetValue(key, out var s);
                return s;
            }

            set
            {
                if (m_logContext == null)
                    m_logContext = new Dictionary<string, string>();

                m_logContext[key] = value;

            }
        }

        /// <summary>
        /// Gets the keys in this log context
        /// </summary>
        /// <value>The context keys.</value>
        public IEnumerable<string> ContextKeys
        {
            get
            {
                if (m_logContext == null)
                    return new string[0];

                return m_logContext.Keys;
            }
        }

        /// <summary>
        /// Gets the message, formatted with arguments
        /// </summary>
        /// <value>The formatted message.</value>
        public string FormattedMessage
        {
            get
            {
                if (Arguments == null || Arguments.Length == 0)
                    return Message;

                try
                {
                    return string.Format(Message, Arguments);
                }
                catch
                {
                    // Try no to crash ...
                    return string.Format("Error while formating: \"{0}\" with arguments: [{1}]", Message, string.Join(", ", Arguments.Select(x => x == null ? "(null)" : x.ToString())));

                }
            }
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Logging.LogEntry"/> class.
        /// </summary>
        /// <param name="message">The message to use.</param>
        /// <param name="arguments">An optional set of arguments.</param>
        /// <param name="level">The log level.</param>
        /// <param name="tag">The tag to use.</param>
        /// <param name="id">The message ID.</param>
        /// <param name="exception">An optional exception.</param>
        public LogEntry(string message, object[] arguments, LogMessageType level, string tag, string id, Exception exception)
        {
            When = DateTime.Now;
            Message = message;
            Arguments = arguments;
            Level = level;
            Tag = tag;
            Id = id;
            Exception = exception;
            FilterTag = level + "-" + tag + "-" + id;
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:Duplicati.Library.Logging.LogEntry"/>.
        /// </summary>
        /// <returns>A <see cref="T:System.String"/> that represents the current <see cref="T:Duplicati.Library.Logging.LogEntry"/>.</returns>
        public override string ToString()
        {
            return string.Format("{0:yyyy-MM-dd HH:mm:ss zz} - [{1}]: {2}", When.ToLocalTime(), FilterTag, FormattedMessage);
        }

        /// <summary>
        /// Returns the item as a string optionally with exception details
        /// </summary>
        /// <returns>A <see cref="T:System.String"/> that represents the current <see cref="T:Duplicati.Library.Logging.LogEntry"/>.</returns>
        /// <param name="withExceptionDetails">If set to <c>true</c> the result has expanded exception details.</param>
        public string AsString(bool withExceptionDetails)
        {
            return this +
                       ((withExceptionDetails && Exception != null)
                        ? Environment.NewLine + Exception
                        : string.Empty);
        }
    }
}
