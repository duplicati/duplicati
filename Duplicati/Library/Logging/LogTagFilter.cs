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
