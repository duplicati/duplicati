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
namespace Duplicati.Library.Logging
{
    /// <summary>
    /// A log destinations that invokes a method
    /// </summary>
    internal class FunctionLogDestination : ILogDestination
    {
        /// <summary>
        /// The method to call for logging
        /// </summary>
        private readonly Action<LogEntry> m_entry;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Logging.FunctionLogDestination"/> class.
        /// </summary>
        /// <param name="entry">The method to call for logging.</param>
        public FunctionLogDestination(Action<LogEntry> entry)
        {
            m_entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        /// <summary>
        /// Writes the message by invoking the handler.
        /// </summary>
        /// <param name="entry">The entry to log.</param>
		public void WriteMessage(LogEntry entry)
        {
            m_entry.Invoke(entry);
        }
    }

    /// <summary>
    /// A log filter that wraps a callback method
    /// </summary>
    internal class FunctionFilter : ILogFilter
    {
        /// <summary>
        /// The method to call for filtering
        /// </summary>
        private readonly Func<LogEntry, bool> m_handler;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Logging.FunctionFilter"/> class.
        /// </summary>
        /// <param name="handler">The method to call for filtering.</param>
        public FunctionFilter(Func<LogEntry, bool> handler)
        {
            m_handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Determines if the element is accepted or nor
        /// </summary>
        /// <returns>A value indicating if the value is accepted or not.</returns>
        /// <param name="entry">The log entry to filter.</param>
        public bool Accepts(LogEntry entry)
        {
            return m_handler(entry);
        }
    }
}
