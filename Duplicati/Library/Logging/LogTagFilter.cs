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
using System.Linq;

namespace Duplicati.Library.Logging
{
    /// <summary>
    /// Implements a filter that removes log messages based on tags
    /// </summary>
    public class LogTagFilter : ILogFilter
    {
        /// <summary>
        /// The tag prefixes we exclude
        /// </summary>
        private readonly string[] m_excludeTags;

        /// <summary>
        /// The tag prefixes we include
        /// </summary>
        private readonly string[] m_includeTags;

        /// <summary>
        /// The minimum log level to consider
        /// </summary>
        private readonly LogMessageType m_logLevel;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Logging.LogTagFilter"/> class.
        /// </summary>
        public LogTagFilter(LogMessageType logLevel, string[] excludeTags, string[] includeTags)
        {
            m_logLevel = logLevel;
            m_excludeTags = excludeTags ?? new string[0];
            m_includeTags = includeTags ?? new string[0];
        }

        /// <summary>
        /// A method called to determine if a given message should be filtered or not
        /// </summary>
        /// <returns><c>true</c> if the message is included; <c>false</c> otherwise</returns>
        /// <param name="entry">The entry to examine</param>
        public bool Accepts(LogEntry entry)
        {
            // Skip all explicitly excluded
            if (m_excludeTags.Any(x => entry.FilterTag.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Include all explicitly included or under threshold
            return entry.Level >= m_logLevel || m_includeTags.Any(x => entry.FilterTag.StartsWith(x, StringComparison.OrdinalIgnoreCase));
        }
    }
}
