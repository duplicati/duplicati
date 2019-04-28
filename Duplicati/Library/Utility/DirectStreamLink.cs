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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// A link connecting a streaming writer to a streaming reader through a buffer.
    /// Should be used to enable multithreading by linking sequential processes avoiding tempfiles (piping).
    /// Safe for a single reader and writer to be in different threads. It is possible to pass the written
    /// data on to an underlying stream. Use another DirectLinkStream as passthrough to feed multiple spawned
    /// threads processing data.
    /// </summary>
    public class DirectStreamLink : IDisposable
    {
        /// <summary>
        /// Main lock to synchronize operations between reader and writer.
        /// Used to protect accesses to all state vars and making decisions
        /// on blocking thereon.
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary> An event to wake reader. </summary>
        private readonly ManualResetEventSlim m_signalDataAvailable = new ManualResetEventSlim(false);
        /// <summary> An event to wake writer. </summary>
        private readonly ManualResetEventSlim m_signalBufferAvailable = new ManualResetEventSlim(true);
        /// <summary> Track closes and enable threadsafe self dispose. </summary>
        private int m_autoDisposeCounter = 2;

        /// <summary> Number of bytes written to buffer. </summary>
        private long m_written = 0;
        /// <summary> Number of bytes read from buffer. </summary>
        private long m_read = 0;
        /// <summary> If the writer is closed. </summary>
        private volatile bool m_writerClosed = false;
        /// <summary> If the reader is closed. </summary>
        private volatile bool m_readerClosed = false;

        /// <summary> The buffer for piping. </summary>
        private readonly byte[] m_buf;

        /// <summary> A stream to pass writes to. For stream stacking. </summary>
        private readonly Stream m_passWriteThrough;

        /// <summary> Allows to set a length for SubStreams Length property. </summary>
        private long m_knownLength = -1;
        /// <summary>
        /// If set the length is enforced as follows:
        /// EndOfStreamException On Write() if writer tries to write more bytes.
        /// EndOfStreamException On Read() if reader tries to read more bytes after writer has closed before knownLength.
        /// </summary>
        private bool m_enforceKnownLength = false;

        /// <summary>
        /// Forces to wait until reader has consumed all bytes from buffer
        /// on a Flush operation. Set via constructor.
        /// </summary>
        private readonly bool m_blockOnFlush = true;

        /// <summary>
        /// Forces to wait until reader has closed its stream
        /// on writer's Close operation. This may be used to 
        /// synchronize worker threads. Set via constructor.
        /// </summary>
        private readonly bool m_blockOnClose = true;

        /// <summary> The helper stream for reader from pipe. </summary>
        private LinkedReaderStream m_readerStream;
        /// <summary> The helper stream for writer to pipe. </summary>
        private LinkedWriterStream m_writerStream;


        /// <summary> Sets up the DirectStreamLink with a certain behaviour. </summary>
        /// <param name="bufsize"> The size of the internal  buffer to use. </param>
        /// <param name="blockOnFlush"> Specifies to block Flush until reader has read buffer empty. This is not suited for synching. </param>
        /// <param name="blockOnClose"> Specifies to block close of writer until reader has closed. Can be used for synching. </param>
        /// <param name="passWriteThrough"> A stream all written data is just passed to. For stream stacking. </param>
        public DirectStreamLink(int bufsize, bool blockOnFlush, bool blockOnClose, Stream passWriteThrough)
        {
            m_passWriteThrough = passWriteThrough;
            m_blockOnFlush = blockOnFlush;
            m_blockOnClose = blockOnClose;
            if (bufsize <= 0) throw new ArgumentOutOfRangeException(nameof(bufsize), "The size of the buffer must be positive.");
            m_buf = new byte[bufsize];
            m_readerStream = new LinkedReaderStream(this);
            m_writerStream = new LinkedWriterStream(this);
        }

        /// <summary> The Stream to read from the link. </summary>
        public Stream ReaderStream { get { return m_readerStream; } }
        /// <summary> The Stream to write to the link. </summary>
        public Stream WriterStream { get { return m_writerStream; } }

        /// <summary> 
        /// Allows to set and optionally enforce the length of the piped data if known before.
        /// This may help consumers that need a length for correct operation.
        /// Set length to negative value if length is not known (default).
        /// </summary>
        public void SetKnownLength (long length, bool enforce)
        {
            lock (m_lock)
            {
                m_knownLength = length;
                m_enforceKnownLength = enforce;
            }
        }

        /// <summary> Read bytes from the Pipe. Blocks if none available. Redirected from ReaderStream. </summary>
        private int read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;
            while (count > 0)
            {
                int readBytes = 0;
                int startIndex = 0;
                int bufFilled;

                lock (m_lock)
                {
                    bufFilled = (int)(m_written - m_read);
                    if (bufFilled == 0)
                    {
                        if (bytesRead > 0) return bytesRead; // we have data, return it instead of blocking.
                        if (m_writerClosed)
                        {
                            if (m_enforceKnownLength && m_read < m_knownLength) throw new EndOfStreamException();
                            else return bytesRead; // stream is done.
                        }
                        m_signalDataAvailable.Reset(); // block and wait for data
                    }
                    else
                    {
                        startIndex = (int)(m_read % m_buf.Length);
                        readBytes = m_buf.Length - startIndex; // maximum to end of buffer
                        if (count < readBytes) readBytes = count;
                        if (bufFilled < readBytes) { readBytes = bufFilled; count = bufFilled; }
                    }
                }
                if (readBytes == 0) m_signalDataAvailable.Wait();
                else
                {
                    Array.Copy(m_buf, startIndex, buffer, offset + bytesRead, readBytes);
                    bytesRead += readBytes;
                    count -= readBytes;
                    lock (m_lock)
                    {
                        m_read += readBytes;
                        m_signalBufferAvailable.Set();
                    }
                }
            }
            return bytesRead;
        }

        /// <summary> Write bytes to the Pipe. Blocks if buffer full. Redirected from WriterStream. </summary>
        private void write(byte[] buffer, int offset, int count)
        {
            int orgOffset = offset, orgCount = count;
            while (count > 0)
            {
                int writeBytes = 0;
                int startIndex = 0;
                int bufFree;

                lock (m_lock)
                {
                    if (m_enforceKnownLength && m_knownLength >= 0 && (m_written + count) > m_knownLength)
                        throw new EndOfStreamException();

                    if (m_readerClosed) return; // we do not care about writes after reader has closed his stream (Note: PassThrough is still done).
                    bufFree = (int)(m_buf.Length - (m_written - m_read));
                    if (bufFree == 0) m_signalBufferAvailable.Reset();
                    else
                    {
                        startIndex = (int)(m_written % m_buf.Length);
                        writeBytes = m_buf.Length - startIndex; // maximum to end of buffer
                        if (bufFree < writeBytes) writeBytes = bufFree;
                    }
                }
                if (writeBytes == 0) m_signalBufferAvailable.Wait();
                else
                {
                    if (count < writeBytes) writeBytes = count;
                    Array.Copy(buffer, offset, m_buf, startIndex, writeBytes);
                    offset += writeBytes;
                    count -= writeBytes;
                    lock (m_lock)
                    {
                        m_written += writeBytes;
                        m_signalDataAvailable.Set();
                    }
                }
            }

            // We pass through the data after written to our own buffer.
            // This allows our worker to start processing.
            // If we block because our worker cannot consume fast enough,
            // We are the slower part in the chain anyway.
            if (m_passWriteThrough != null)
                m_passWriteThrough.Write(buffer, orgOffset, orgCount);
        }

        private void flush()
        {
            if (m_passWriteThrough != null)
                m_passWriteThrough.Flush();

            if (m_blockOnFlush) // wait until reader has consumed last chunk of data
            {
                bool isEmpty = false;
                while (!isEmpty && !m_readerClosed)
                {
                    lock (m_lock)
                    {
                        isEmpty = !(m_read < m_written);
                        if (!isEmpty && !m_readerClosed) m_signalBufferAvailable.Reset();
                    }
                    m_signalBufferAvailable.Wait();
                }
            }
        }

        private void readerClosed()
        {
            lock (m_lock)
            {
                if (m_readerClosed) return;
                m_readerClosed = true;
                m_signalBufferAvailable.Set(); // unblock potentially waiting writer
                m_readerStream = null;
            }

            if (Interlocked.Decrement(ref m_autoDisposeCounter) == 0)
                this.Dispose();
        }

        private void writerClosed()
        {
            lock (m_lock)
            {
                if (m_writerClosed) return;
                m_writerClosed = true;
                m_signalDataAvailable.Set(); // unblock potentially waiting reader
                m_writerStream = null;
            }

            flush();

            // we close m_passWriteThrough before blocking, so if at end of chain (stacked DirectStreamLinks) 
            // a blocked reader is waiting, it can proceed sooner.
            if (m_passWriteThrough != null)
            { m_passWriteThrough.Close(); }

            if (m_blockOnClose) // wait until reader has closed its stream before continuing.
            {
                while (!m_readerClosed)
                {
                    lock (m_lock)
                    {
                        if (!m_readerClosed) m_signalBufferAvailable.Reset();
                    }
                    m_signalBufferAvailable.Wait();
                }
            }
            
            if (Interlocked.Decrement(ref m_autoDisposeCounter) == 0)
                this.Dispose();
        }

        /// <summary> Disposes class. Is triggered automatically as soon as reader and writer are closed. </summary>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (!m_readerClosed && m_readerStream != null) m_readerStream.Close();
                if (!m_writerClosed && m_writerStream != null) m_writerStream.Close();
            }

            this.m_signalBufferAvailable.Dispose();
            this.m_signalDataAvailable.Dispose();
        }

        #region HelperClasses: Reader, Writer and a DataPump

        /// <summary> Common base class for reader and writer. </summary>
        private abstract class LinkedSubStream : Stream
        {
            protected DirectStreamLink m_linkStream;
            protected LinkedSubStream(DirectStreamLink linkStream)
            { this.m_linkStream = linkStream; }

            public override bool CanSeek { get { return false; } }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override long Length { get { if (m_linkStream.m_knownLength >= 0) return m_linkStream.m_knownLength; else throw new NotSupportedException(); } }

            // We fake Seek and Position to at least support dummy operations.
            // That mitigates some things like setting Position = 0 on start
            // and if callers rely on Position instead of counting themselves.
            public override long Seek(long offset, SeekOrigin origin)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin: return this.Position = offset;
                    case SeekOrigin.Current: return this.Position = this.Position + offset;
                    default: throw new NotSupportedException();
                }
            }
        }


        /// <summary> The class for readig from DirectStreamLink. </summary>
        private class LinkedReaderStream : LinkedSubStream
        {
            public LinkedReaderStream(DirectStreamLink linkStream)
                : base(linkStream) { }
            public override bool CanRead { get { return true; } }
            public override bool CanWrite { get { return false; } }
            public override long Position
            {
                get { return m_linkStream.m_read; }
                set { throw new NotSupportedException(); }
            }
            public override int Read(byte[] buffer, int offset, int count)
            { return m_linkStream.read(buffer, offset, count); }
            public override void Write(byte[] buffer, int offset, int count)
            { throw new NotSupportedException(); }

            protected override void Dispose(bool disposing)
            {
                m_linkStream.readerClosed();
                base.Dispose(disposing);
            }
            public override void Flush() { }

        }

        /// <summary> The class for writing to DirectStreamLink. </summary>
        private class LinkedWriterStream : LinkedSubStream
        {
            public LinkedWriterStream(DirectStreamLink linkStream)
                : base(linkStream) { }
            public override bool CanRead { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Position
            {
                get { return m_linkStream.m_written; }
                set { throw new NotSupportedException(); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count)
            { m_linkStream.write(buffer, offset, count); }
            protected override void Dispose(bool disposing)
            {
                m_linkStream.writerClosed();
                base.Dispose(disposing);
            }
            public override void Flush() { m_linkStream.flush(); }

        }


        /// <summary> 
        /// A helper class to transfer data asynchronously from one stream into another.
        /// Handles exceptions, takes care of closes when done and is suited to be used
        /// with syncing-on-Close in DirectStreamLink's.
        /// When data is transferred, streams are closed by default (see constructor).
        /// </summary>
        public class DataPump
        {
            /// <summary> Default buffer size for pumping </summary>
            public const int DEFAULTBUFSIZE = 1 << 14; // 16K

            private readonly bool m_closeInputWhenDone, m_closeOutputWhenDone;
            private readonly Action<DataPump> m_callbackFinalizePumping = null;
            private Stream m_input, m_output;

            private long m_count = 0;
            private volatile bool m_wasStarted = false;

            /// <summary> Creates and configures a new DataPump instance. </summary>
            /// <param name="input"> The stream to read data from. </param>
            /// <param name="output"> The stream to write data to. </param>
            /// <param name="callbackFinalizePumping"> A callback to issue when pumping is done but before streams are closed. e.g. Can add data to output. </param>
            /// <param name="dontCloseInputWhenDone"> Disable auto close of input stream when pumping is done. </param>
            /// <param name="dontCloseOutputWhenDone"> Disable auto close of output stream when pumping is done. </param>
            public DataPump(Stream input, Stream output, Action<DataPump> callbackFinalizePumping = null
                , bool dontCloseInputWhenDone = false, bool dontCloseOutputWhenDone = false)
            {
                this.m_input = input;
                this.m_output = output;
                this.m_callbackFinalizePumping = callbackFinalizePumping;
                this.m_closeInputWhenDone = !dontCloseInputWhenDone;
                this.m_closeOutputWhenDone = !dontCloseOutputWhenDone;
            }

            /// <summary> Returns number of bytes currently transferred. </summary>
            public long BytesPumped
            { get { return System.Threading.Interlocked.Read(ref m_count); } }
            /// <summary> Returns if the DataPump was started. </summary>
            public bool WasStarted { get { return m_wasStarted; } }

            /// <summary>
            /// Runs DataPump blocking. Can be used with Task class when calling from 
            /// higher .Net Framework. Returns number of bytes pumped. Rethrows exceptions
            /// if any on Read() or Write().
            /// </summary>
            public long Run()
            {
                if (m_wasStarted)
                    throw new InvalidOperationException();
                m_wasStarted = true;
                return doRun(true);
            }

            /// <summary> Actually transfers stream data. </summary>
            private long doRun(bool rethrowException)
            {
                byte[] buf = new byte[1 << 14]; int c;
                try
                {
                    while ((c = m_input.Read(buf, 0, buf.Length)) > 0)
                    {
                        m_output.Write(buf, 0, c);
                        System.Threading.Interlocked.Add(ref m_count, c);
                    }

                    if (m_callbackFinalizePumping != null)
                    {
                        try { m_callbackFinalizePumping(this); }
                        catch { }
                    }
                }
                catch (Exception)
                {
                    if (rethrowException) throw;
                }
                finally
                {
                    // When done, close streams and clear references.
                    // We close output first. This is on purpose to mitigate potential race conditions
                    // when a caller is synchronizing against close of input stream.
                    // This is commonly the case when DataPump is used to decouple two previously stacked streams
                    // through DirectLinkStream with BlockOnClose option.
                    try { if (m_output != null && m_closeOutputWhenDone) m_output.Close(); }
                    catch { }
                    try { if (m_input != null && m_closeInputWhenDone) m_input.Close(); }
                    catch { }

                    m_input = m_output = null;
                }
                return m_count;
            }
        }

        #endregion
    }
}
