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
    /// Writes log messages to a stream
    /// </summary>
    public class StreamLog : ILog, IDisposable
    {
        private System.IO.StreamWriter m_stream;

        /// <summary>
        /// Constructs a new log destination, writing to the supplied stream
        /// </summary>
        /// <param name="stream">The stream to write log messages into</param>
        public StreamLog(System.IO.Stream stream)
        {
            m_stream = new System.IO.StreamWriter(stream);
        }

        /// <summary>
        /// Constructs a new log destination, writing to the supplied file
        /// </summary>
        /// <param name="filename">The file to write to</param>
        public StreamLog(string filename)
            : this(new System.IO.FileStream(filename, System.IO.FileMode.Append, System.IO.FileAccess.Write))
        {
        }

        #region ILog Members

        /// <summary>
        /// The function called when a message is logged
        /// </summary>
        /// <param name="message">The message logged</param>
        /// <param name="type">The type of message logged</param>
        /// <param name="exception">An exception, may be null</param>
        public virtual void WriteMessage(string message, LogMessageType type, Exception exception)
        {
            m_stream.WriteLine(type.ToString() + ": " + message);
            if (exception != null)
            {
                m_stream.WriteLine(exception.ToString());
                m_stream.WriteLine();
            }

            if (EventLogged != null)
                EventLogged(message, type, exception);
        }

        /// <summary>
        /// An event that is raised when a message is logged
        /// </summary>
        public event EventLoggedDelgate EventLogged;

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Frees up all internally held resources
        /// </summary>
        public void Dispose()
        {
            try { if (m_stream != null) m_stream.Flush(); }
            catch { }
            try { if (m_stream != null) m_stream.Close(); }
            catch { }

            m_stream = null;
        }

        #endregion
    }
}
