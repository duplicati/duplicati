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
using System.IO;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine
{
    /// <summary>
    /// Log output handler for the console
    /// </summary>
    public class ConsoleLogTarget : Library.Logging.ILogDestination
    {
        /// <summary>
        /// The stdout stream
        /// </summary>
        private readonly TextWriter m_stdout;
        /// <summary>
        /// The stderr stream
        /// </summary>
        private readonly TextWriter m_stderr;
        /// <summary>
        /// The default log level
        /// </summary>
        private readonly LogMessageType m_level;
        /// <summary>
        /// The filter, if any
        /// </summary>
        private readonly IFilter m_filter;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.CommandLine.ConsoleLogTarget"/> class.
        /// </summary>
        /// <param name="stdout">The stdout stream.</param>
        /// <param name="stderr">The stderr stream.</param>
        /// <param name="level">The minimum log level to consider.</param>
        /// <param name="logfilter">The log filter, if any.</param>
        public ConsoleLogTarget(TextWriter stdout, TextWriter stderr, LogMessageType level, string logfilter)
        {
            m_stdout = stdout;
            m_stderr = stderr;
            m_level = level;
            m_filter = FilterExpression.ParseLogFilter(logfilter);
        }

        /// <summary>
        /// Writes the message to stdout or stderr.
        /// </summary>
        /// <param name="entry">The entry to write.</param>
        public void WriteMessage(LogEntry entry)
        {
            var found = m_filter.Matches(entry.FilterTag, out var result, out var match);
            // If there is a filter match, use that
            if (found)
            {
                if (!result)
                    return;
                
                if (entry.Level == LogMessageType.Error)
                    m_stderr.WriteLine(entry.ToString());
                else
                    m_stdout.WriteLine(entry.ToString());
            }
            else
            {
                // Otherwise, filter by log-level
                if (entry.Level < m_level)
                    return;
            }

            if (entry.Level == LogMessageType.Error)
                m_stderr.WriteLine(entry.FormattedMessage);
            else
                m_stdout.WriteLine(entry.FormattedMessage);

        }
    }
}
