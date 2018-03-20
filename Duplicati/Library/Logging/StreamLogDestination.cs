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
    /// Writes log messages to a stream
    /// </summary>
    public class StreamLogDestination : ILogDestination, IDisposable
    {
        /// <summary>
        /// The stream to log to
        /// </summary>
        private System.IO.StreamWriter m_stream;

        /// <summary>
        /// Constructs a new log destination, writing to the supplied stream
        /// </summary>
        /// <param name="stream">The stream to write log messages into</param>
        public StreamLogDestination(System.IO.Stream stream)
        {
            m_stream = new System.IO.StreamWriter(stream);
            m_stream.AutoFlush = true;
        }

        /// <summary>
        /// Constructs a new log destination, writing to the supplied file
        /// </summary>
        /// <param name="filename">The file to write to</param>
        public StreamLogDestination(string filename)
            : this(new System.IO.FileStream(filename, System.IO.FileMode.Append, System.IO.FileAccess.Write))
        {
        }

        #region ILog Members

        /// <summary>
        /// The function called when a message is logged
        /// </summary>
        /// <param name="entry">The entry to write</param>
        public virtual void WriteMessage(LogEntry entry)
        {

            /*m_stream.WriteLine("{0:u} - {1}: {2}", entry.When.ToLocalTime(), entry.Level, entry.FormattedMessage);
            if (entry.Exception != null)
            {
                m_stream.WriteLine(entry.Exception);
                m_stream.WriteLine();
            }*/

            m_stream.WriteLine(entry.AsString(true));
        }
            
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
