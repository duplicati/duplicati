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
    /// Performs timing of operations, and writes the result to the log
    /// </summary>
    public class Timer : IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = "Timer";

        private readonly DateTime m_begin;
        private string m_operation;
        private readonly string m_logtag;
        private readonly string m_logid;

        /// <summary>
        /// Constructs a new timer, and starts timing
        /// </summary>
        /// <param name="operation">The name of the operation being performed</param>
        /// <param name="logtag">The log tag addition to use</param>
        /// <param name="logid">The ID for this log message</param>
        public Timer(string logtag, string logid, string operation)
        {
            m_operation = operation;
            m_begin = DateTime.Now;
            m_logtag = logtag;
            m_logid = logid;
            Log.WriteProfilingMessage(LOGTAG + ".Begin-" + m_logtag, m_logid, "Starting - {0}", m_operation);
        }

        #region IDisposable Members

        /// <summary>
        /// Frees up all internally held resources
        /// </summary>
        public void Dispose()
        {
            if (m_operation == null)
                return;

            Log.WriteProfilingMessage(LOGTAG + ".Finished-" + m_logtag, m_logid, "{0} took {1:d\\:hh\\:mm\\:ss\\.fff}", m_operation, DateTime.Now - m_begin);
            m_operation = null;
        }

        #endregion
    }
}
