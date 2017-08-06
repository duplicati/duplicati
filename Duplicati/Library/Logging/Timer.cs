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
using System.Diagnostics;
using System.Text;

namespace Duplicati.Library.Logging
{
    /// <summary>
    /// Performs timing of operations, and writes the result to the log
    /// </summary>
    public class Timer : IDisposable
    {
        private static readonly long nanosPerTick = (1000000000L / Stopwatch.Frequency);

        private Stopwatch m_stopwatch;
        private string m_operation;

        /// <summary>
        /// Constructs a new timer, and starts timing
        /// </summary>
        /// <param name="operation">The name of the operation being performed</param>
        public Timer(string operation)
        {
            if (Log.LogLevel == LogMessageType.Profiling)
            {
                m_operation = operation;
                m_stopwatch = Stopwatch.StartNew();

                Log.WriteMessage(string.Format("Starting - {0}", m_operation), LogMessageType.Profiling);
            }
        }

        private static long getElapsedMicros(Stopwatch stopwatch)
        {
            return stopwatch.ElapsedTicks * nanosPerTick / 1000;
        }

        #region IDisposable Members

        /// <summary>
        /// Frees up all internally held resources
        /// </summary>
        public void Dispose()
        {
            if (m_operation == null)
                return;

            if (Log.LogLevel == LogMessageType.Profiling)
                Log.WriteMessage(string.Format("{0} took {1} microseconds", m_operation, getElapsedMicros(m_stopwatch)), LogMessageType.Profiling);
            m_operation = null;
        }
        #endregion
    }
}
