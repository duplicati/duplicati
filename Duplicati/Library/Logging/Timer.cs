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
