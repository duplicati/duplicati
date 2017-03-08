//  Copyright (C) 2017, The Duplicati Team
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
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Compression
{
    public class SharedStream : Stream
    {
        private readonly object m_lock = new object();
        private bool m_finished = false;
        private readonly MemoryStream m_buffer = new MemoryStream();
        private ManualResetEvent m_write_event = new ManualResetEvent(false);
        private ManualResetEvent m_read_event = new ManualResetEvent(false);
        private const int MAX_BYTES_BUFFER = 1024 * 1024;
        private long m_read_pos = 0;
        private long m_write_pos = 0;
        private int m_read_offset = 0;
        private readonly Task m_runner;
        public SharedStream(Action<SharedStream> m = null)
        {
            m_runner = 
                m == null 
                ? Task.FromResult(true)
                : Task.Run(() => m(this));
        }

        public override bool CanRead
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return true;
            }
        }

        public override long Length
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override long Position
        {
            get
            {
                return m_read_pos;
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public override void Flush()
        {
            
        }

        private long BytesLeft
        {
            get
            {
                return m_write_pos - m_read_pos;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = 0;
            while (true)
            {
                lock (m_lock)
                {
                    if (BytesLeft > 0)
                    {
                        m_buffer.Position = m_read_offset;
                        var res = m_buffer.Read(buffer, offset, count);
                        read += res;
                        m_read_pos += res;
                        m_read_offset += res;

                        if (BytesLeft == 0)
                        {
                            m_buffer.SetLength(0);
                            m_read_event.Set();
                            m_read_offset = 0;
                        }

                        if (m_finished || read == count)
                            return read;
                    }

                    m_write_event.Reset();
                }

                if (m_finished)
                    return 0;

                m_write_event.WaitOne(1000, true);
            }

        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            while (true)
            {
                lock (m_lock)
                {
                    if (BytesLeft < MAX_BYTES_BUFFER || m_finished)
                    {
                        m_buffer.Position = m_buffer.Length;
                        m_buffer.Write(buffer, offset, count);
                        m_write_event.Set();
                        m_write_pos += count;
                        return;
                    }

                    m_read_event.Reset();
                }

                m_read_event.WaitOne(1000, true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            var wait = false;
            lock(m_lock)
            {
                if (!m_finished)
                {
                    m_finished = true;
                    m_read_event.Set();
                    m_write_event.Set();
                    base.Dispose(disposing);
                    wait = true;
                }
            }

            if (wait)
                m_runner.Wait();
        }
    }
}
