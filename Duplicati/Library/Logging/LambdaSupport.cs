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
