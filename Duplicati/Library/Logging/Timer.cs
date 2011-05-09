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
    /// Performs timing of operations, and writes the result to the log
    /// </summary>
    public class Timer : IDisposable
    {
#if DEBUG
        private DateTime m_begin;
        private string m_operation;
#endif
        /// <summary>
        /// Constructs a new timer, and starts timing
        /// </summary>
        /// <param name="operation">The name of the operation being performed</param>
        public Timer(string operation)
        {
#if DEBUG
            m_operation = operation;
            m_begin = DateTime.Now;
#endif
        }

        #region IDisposable Members

        /// <summary>
        /// Frees up all internally held resources
        /// </summary>
        public void Dispose()
        {
#if DEBUG
            if (m_operation == null)
                return;

            TimeSpan ts = DateTime.Now - m_begin;
            string time = ts.Minutes.ToString("00") + ":" + ts.Seconds.ToString("00") + ":" + ts.Milliseconds.ToString("000");

            Log.WriteMessage(m_operation + " took " + time, LogMessageType.Profiling);
            m_operation = null;
#endif
        }

        #endregion
    }
}
