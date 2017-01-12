//  Copyright (C) 2015, The Duplicati Team

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
using System.Linq;
using Duplicati.Library.Logging;
using System.Collections.Generic;

namespace Duplicati.Server
{
    /// <summary>
    /// Class that handles logging from the server, 
    /// and provides an entry point for the runner
    /// to redirect log output to a file
    /// </summary>
    public class LogWriteHandler : ILog, IDisposable
    {
        /// <summary>
        /// The number of messages to keep when inactive
        /// </summary>
        private const int INACTIVE_SIZE = 30;
        /// <summary>
        /// The number of messages to keep when active
        /// </summary>
        private const int ACTIVE_SIZE = 5000;

        /// <summary>
        /// Represents a single log event
        /// </summary>
        public struct LogEntry
        {
            /// <summary>
            /// A unique ID that sequentially increments
            /// </summary>
            private static long _id;

            /// <summary>
            /// The time the message was logged
            /// </summary>
            public DateTime When;

            /// <summary>
            /// The ID assigned to the message
            /// </summary>
            public long ID;

            /// <summary>
            /// The logged message
            /// </summary>
            public string Message;

            /// <summary>
            /// The message type
            /// </summary>
            public LogMessageType Type;

            /// <summary>
            /// Exception data attached to the message
            /// </summary>
            public Exception Exception;

            /// <summary>
            /// Initializes a new instance of the <see cref="Duplicati.Server.LogWriteHandler+LogEntry"/> struct.
            /// </summary>
            /// <param name="message">Message.</param>
            /// <param name="type">Type.</param>
            /// <param name="exception">Exception.</param>
            public LogEntry(string message, LogMessageType type, Exception exception)
            {
                this.ID = System.Threading.Interlocked.Increment(ref _id);
                this.When = DateTime.UtcNow;
                this.Message = message;
                this.Type = type;
                this.Exception = exception;
            }
        }

        /// <summary>
        /// Basic implementation of a ring-buffer
        /// </summary>
        private class RingBuffer<T> : IEnumerable<T>
        {
            private T[] m_buffer;
            private int m_head;
            private int m_tail;
            private int m_length;
            private int m_key;
            private object m_lock = new object();

            public RingBuffer(int size, IEnumerable<T> initial = null)
            {
                m_buffer = new T[size];
                if (initial != null)
                    foreach(var t in initial)
                        this.Enqueue(t);
            }
                
            public int Length { get { return m_length; } }

            public T Dequeue()
            {
                lock(m_lock)
                {
                    if (m_length == 0)
                        throw new ArgumentOutOfRangeException("Buffer is empty");
                    
                    m_key++;
                    var ix = m_tail;
                    m_tail = (m_tail + 1) % m_buffer.Length;
                    m_length--;

                    return m_buffer[ix];
                }
            }

            public void Enqueue(T item)
            {
                lock(m_lock)
                {
                    m_key++;
                    m_buffer[m_head] = item;
                    m_head = (m_head + 1) % m_buffer.Length;
                    if (m_length == m_buffer.Length)
                        m_tail = (m_tail + 1) % m_buffer.Length;
                    else
                        m_length++;
                }
            }

            #region IEnumerable implementation
            public IEnumerator<T> GetEnumerator()
            {
                var k = m_key;
                for(var i = 0; i < m_length; i++)
                    if (m_key != k)
                        throw new InvalidOperationException("Buffer was modified while reading");
                    else
                        yield return m_buffer[(m_tail + i) % m_buffer.Length];
            }
            #endregion
            #region IEnumerable implementation
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
            #endregion

            public T[] FlatArray(Func<T, bool> filter = null)
            {
                lock(m_lock)
                    if (filter == null)
                        return this.ToArray();
                    else
                        return this.Where(filter).ToArray();
            }

            public void Clear()
            {
                lock(m_lock)
                {
                    m_length = 0;
                    m_tail = 0;
                    m_head = 0;
                    m_buffer = new T[m_buffer.Length];
                }
            }

            public int Size { get { return m_buffer.Length; } }
        }

        private DateTime[] m_timeouts;
        private object m_lock = new object();
        private volatile bool m_anytimeouts = false;
        private RingBuffer<LogEntry> m_buffer;

        private ILog m_serverfile;
        private LogMessageType m_serverloglevel;

        public LogWriteHandler()
        {
            var fields = Enum.GetValues(typeof(LogMessageType));
            m_timeouts = new DateTime[fields.Length];
            m_buffer = new RingBuffer<LogEntry>(INACTIVE_SIZE);
        }

        public void RenewTimeout(LogMessageType type)
        {
            lock(m_lock)
            {
                m_timeouts[(int)type] = DateTime.Now.AddSeconds(30);
                m_anytimeouts = true;
                if (m_buffer == null || m_buffer.Size == INACTIVE_SIZE)
                    m_buffer = new RingBuffer<LogEntry>(ACTIVE_SIZE, m_buffer);
            }
        }

        public void SetServerFile(string path, LogMessageType level)
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            m_serverfile = new StreamLog(path);
            m_serverloglevel = level;

            UpdateLogLevel();
        }

        public LogEntry[] AfterTime(DateTime offset, LogMessageType level)
        {
            RenewTimeout(level);
            UpdateLogLevel();

            offset = offset.ToUniversalTime();
            lock(m_lock)
            {
                if (m_buffer == null)
                    return new LogEntry[0];
                
                return m_buffer.FlatArray((x) => x.When > offset && x.Type >= level );
            }
        }

        public LogEntry[] AfterID(long id, LogMessageType level)
        {
            RenewTimeout(level);
            UpdateLogLevel();

            lock(m_lock)
            {
                if (m_buffer == null)
                    return new LogEntry[0];
                
                return m_buffer.FlatArray((x) => x.ID > id && x.Type >= level );
            }
        }

        private int[] GetActiveTimeouts()
        {
            var i = 0;
            return (from n in m_timeouts
                                let ix = i++
                                where n > DateTime.Now
                                select ix).ToArray();
        }

        private void UpdateLogLevel()
        {
            Duplicati.Library.Logging.Log.LogLevel = 
                (LogMessageType)(GetActiveTimeouts().Union(new int[] { (int)m_serverloglevel }).Min());
        }


        #region ILog implementation

        public void WriteMessage(string message, LogMessageType type, Exception exception)
        {
            if (m_serverfile != null && type >= m_serverloglevel)
                try
                {
                    m_serverfile.WriteMessage(message, type, exception);
                }
                catch
                {
                }

            lock(m_lock)
            {
                if (m_anytimeouts)
                {
                    var q = GetActiveTimeouts();

                    if (q.Length == 0)
                    {
                        UpdateLogLevel();
                        m_anytimeouts = false;
                        if (m_buffer == null || m_buffer.Size != INACTIVE_SIZE)
                            m_buffer = new RingBuffer<LogEntry>(INACTIVE_SIZE, m_buffer);

                    }
                }

                if (m_buffer != null)
                    m_buffer.Enqueue(new LogEntry(message, type, exception));
            }

        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
            if (m_serverfile != null)
            {
                var sf = m_serverfile;
                m_serverfile = null;
                if (sf is IDisposable)
                    ((IDisposable)sf).Dispose();
            }
        }

        #endregion
    }
}

