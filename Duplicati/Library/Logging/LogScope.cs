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
    /// Internal class for keeping log instance relations
    /// </summary>
    internal class LogScope : IDisposable
    {
        /// <summary>
        /// The unique ID of this log instance
        /// </summary>
        public readonly string InstanceID = Guid.NewGuid().ToString();

        /// <summary>
        /// The log instance assigned to this scope
        /// </summary>
        private readonly ILogDestination m_log;

        /// <summary>
        /// The log filter
        /// </summary>
        private readonly ILogFilter m_filter;

        /// <summary>
        /// The log scope parent
        /// </summary>
        public readonly LogScope Parent;

        /// <summary>
        /// A flag indicating if the scope is disposed
        /// </summary>
        private bool m_isDisposed = false;

        /// <summary>
        /// A flag indicating if this is an isolating scope
        /// </summary>
        public readonly bool IsolatingScope;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Logging.LogWrapper"/> class.
        /// </summary>
        /// <param name="self">The log instance to wrap.</param>
        /// <param name="filter">The log filter to use</param>
        /// <param name="parent">The parent scope</param>
        /// <param name="isolatingScope">A flag indicating if the scope is an isolating scope</param>
        public LogScope(ILogDestination self, ILogFilter filter, LogScope parent, bool isolatingScope)
        {
            Parent = parent;

            m_log = self;
            m_filter = filter;
            IsolatingScope = isolatingScope;

            if (parent != null)
                Logging.Log.StartScope(this);
        }

        /// <summary>
        /// The function called when a message is logged
        /// </summary>
        /// <param name="entry">The log entry</param>
        public void WriteMessage(LogEntry entry)
        {
            if (m_log != null && (m_filter == null || m_filter.Accepts(entry)))
                m_log.WriteMessage(entry);
        }

        /// <summary>
        /// Releases all resource used by the <see cref="T:Duplicati.Library.Logging.LogScope"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.Library.Logging.LogScope"/>. The <see cref="Dispose"/> method leaves the
        /// <see cref="T:Duplicati.Library.Logging.LogScope"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.Library.Logging.LogScope"/> so the garbage collector can reclaim the memory that the
        /// <see cref="T:Duplicati.Library.Logging.LogScope"/> was occupying.</remarks>
        public void Dispose()
        {
            if (!m_isDisposed && Parent != null)
            {
                Logging.Log.CloseScope(this);
                m_isDisposed = true;
            }
        }
    }
}
