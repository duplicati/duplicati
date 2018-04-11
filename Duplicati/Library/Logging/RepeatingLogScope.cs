//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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
                await Task.Delay(new TimeSpan(Math.Max(TimeSpan.FromMilliseconds(500).Ticks, remainingTime)));
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
