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
