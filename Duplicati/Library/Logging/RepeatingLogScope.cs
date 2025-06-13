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
using System.Threading.Tasks;

namespace Duplicati.Library.Logging
{
    /// <summary>
    /// Creates a log-scope that repeats the last emitted message, if there is no log activity
    /// </summary>
    public class RepeatingLogScope : ILogDestination, IDisposable
    {
        /// <summary>
        /// A string that is written as the message to the log
        /// </summary>
        private const string STILL_RUNNING = "... ";
        /// <summary>
        /// The scope that we dispose when we are done
        /// </summary>
        private readonly IDisposable m_scope;
        /// <summary>
        /// The max time to wait before repeating the message (in ticks)
        /// </summary>
        private readonly long m_maxIdleTime = TimeSpan.FromSeconds(10).Ticks;
        /// <summary>
        /// The time the last message was written, in ticks
        /// </summary>
        private long m_lastWritten;
        /// <summary>
        /// A flag to keep track of when this instance is disposed
        /// </summary>
        private bool m_completed = false;
        /// <summary>
        /// The last log entry seen
        /// </summary>
        private LogEntry m_lastEntry;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Logging.RepeatingLogScope"/> class.
        /// </summary>
        public RepeatingLogScope()
        {
            m_scope = Log.StartScope(this);
            Task.Run(() => RepeatedRunner());
        }

        /// <summary>
        /// The repeating run task
        /// </summary>
        private async void RepeatedRunner()
        {
            var remainingTime = m_maxIdleTime;
            while (!m_completed)
            {
                await Task.Delay(new TimeSpan(Math.Max(TimeSpan.FromMilliseconds(500).Ticks, remainingTime))).ConfigureAwait(false);
                if (m_completed)
                    return;
                if (m_lastEntry == null)
                    continue;
                
                var now = DateTime.Now.Ticks;
                remainingTime = (m_lastWritten + m_maxIdleTime) - now;

                if (remainingTime < 0)
                {
                    var last = m_lastEntry;
                    var recmsg = STILL_RUNNING + m_lastEntry.FormattedMessage;

                    Logging.Log.WriteMessage(
                        m_lastEntry.Level,
                        m_lastEntry.Tag,
                        m_lastEntry.Id,
                        m_lastEntry.Exception,
                        recmsg,
                        null
                    );

                    // If the last message written is our own message,
                    // don't use the generated one
                    if (object.ReferenceEquals(m_lastEntry.Message, recmsg))
                        m_lastEntry = last;

                    remainingTime = m_maxIdleTime;
                }
            }
        }

        /// <summary>
        /// Releases all resource used by the <see cref="T:Duplicati.Library.Logging.RepeatingLogScope"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.Library.Logging.RepeatingLogScope"/>. The <see cref="Dispose"/> method leaves the
        /// <see cref="T:Duplicati.Library.Logging.RepeatingLogScope"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.Library.Logging.RepeatingLogScope"/> so the garbage collector can reclaim the memory
        /// that the <see cref="T:Duplicati.Library.Logging.RepeatingLogScope"/> was occupying.</remarks>
		public void Dispose()
        {
            m_completed = true;
            m_scope.Dispose();
        }

        /// <summary>
        /// Records the message and updates the timestamp
        /// </summary>
        /// <param name="entry">The entry that is being written.</param>
        public void WriteMessage(LogEntry entry)
        {
            m_lastWritten = DateTime.Now.Ticks;
            m_lastEntry = entry;
        }
    }
}
