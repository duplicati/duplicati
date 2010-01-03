#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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

namespace Duplicati.Library.SharpRSync
{
    /// <summary>
    /// This implements a rolling buffer, used for non-seekable streams
    /// </summary>
    public class RollingBuffer
    {
        private const int BUFFER_SIZE = 1024;
        private int m_headIndex;
        private int m_tailIndex;
        List<byte[]> m_buffers;
        private System.IO.Stream m_stream;

        public RollingBuffer(System.IO.Stream stream)
        {
            m_stream = stream;
            m_buffers = new List<byte[]>();
            m_buffers.Add(new byte[BUFFER_SIZE]);
            m_headIndex = 0;
            m_tailIndex = 0;
        }

        public long Count { get { return m_buffers.Count * BUFFER_SIZE - m_tailIndex - (BUFFER_SIZE - m_headIndex); } }

        /// <summary>
        /// Reads the supplied amount of bytes
        /// </summary>
        /// <param name="count">The number of bytes to read</param>
        /// <returns>The number of bytes actually read</returns>
        public int Advance(int count)
        {
            int totalRead = 0;
            while (count > 0)
            {
                if (m_headIndex == BUFFER_SIZE)
                {
                    m_buffers.Add(new byte[BUFFER_SIZE]);
                    m_headIndex = 0;
                }

                int read = m_stream.Read(m_buffers[m_buffers.Count - 1], m_headIndex, Math.Min(BUFFER_SIZE - m_headIndex, count));
                count -= read;
                m_headIndex += read;
                totalRead += read;
                if (read == 0)
                    break;
            }

            return totalRead;
        }

        /// <summary>
        /// Returns the last read bytes
        /// </summary>
        /// <param name="count">The number of bytes to return</param>
        /// <returns>The bytes requested</returns>
        public byte[] GetHead(long count)
        {
            byte[] tmp = new byte[count];
            GetHead(tmp, 0, count);
            return tmp;
        }


        /// <summary>
        /// Gets the number of avalible bytes in the given buffer
        /// </summary>
        /// <param name="ix">The index for the buffer</param>
        /// <returns>The number of avalible bytes in the buffer</returns>
        private int GetAvalible(int ix)
        {
            if (ix == 0)
                return BUFFER_SIZE - m_tailIndex;
            else if (m_buffers.Count == 0)
                return m_headIndex - m_tailIndex;
            else if (ix == m_buffers.Count - 1)
                return m_headIndex;
            else
                return BUFFER_SIZE;
        }

        /// <summary>
        /// Returns the last read bytes
        /// </summary>
        /// <param name="count">The number of bytes to return</param>
        /// <param name="buf">The buffer to write the bytes into</param>
        /// <param name="offset">The offset into the buffer</param>
        /// <returns>The bytes requested</returns>
        public void GetHead(byte[] buf, long offset, long count)
        {
            int ix = 0;
            long total = this.Count;
            if (total < count)
                throw new Exception(Strings.RollingBuffer.BufferExhaustedError);
            
            int b = m_tailIndex;

            while (total > count)
            {
                int availible = GetAvalible(ix);
                int diff = (int)Math.Min(availible, total - count);
                total -= diff;
                b += diff;

                if (b >= BUFFER_SIZE)
                {
                    ix++;
                    b = 0;
                }
            }

            if (ix == 0)
                b -= m_tailIndex;

            while (count > 0)
            {
                int avalible = (int)Math.Min(GetAvalible(ix) - b, count);
                if (ix == 0)
                    b += m_tailIndex;
                Array.Copy(m_buffers[ix], b, buf, offset, avalible);

                count -= avalible;
                offset += avalible;
                ix++;
                b = 0;
            }
        }

        /// <summary>
        /// Returns the leading bytes
        /// </summary>
        /// <param name="count">The number of bytes to return</param>
        /// <returns>The bytes requested</returns>
        public byte[] GetTail(long count)
        {
            byte[] tmp = new byte[count];
            GetTail(tmp, 0, count);
            return tmp;
        }

        /// <summary>
        /// Returns the leading bytes
        /// </summary>
        /// <param name="count">The number of bytes to return</param>
        /// <param name="buf">The buffer to write the bytes into</param>
        /// <param name="offset">The offset into the buffer</param>
        public void GetTail(byte[] buf, long offset, long count)
        {
            int ix = 0;
            if (this.Count < count)
                throw new Exception(Strings.RollingBuffer.BufferExhaustedError);

            int b = m_tailIndex;
            while (count > 0)
            {
                int avalible = (int)Math.Min(GetAvalible(ix), count);
                Array.Copy(m_buffers[ix], b, buf, offset, avalible);

                count -= avalible;
                offset += avalible;
                ix++;
                b = 0;
            }
        }

        /// <summary>
        /// Removes the bytes from memory
        /// </summary>
        /// <param name="count">The number of bytes to drop</param>
        public void DropTail(long count)
        {
            if (count == this.Count)
            {
                m_buffers = new List<byte[]>();
                m_buffers.Add(new byte[BUFFER_SIZE]);
                m_headIndex = 0;
                m_tailIndex = 0;
                return;
            }

            if (count < (BUFFER_SIZE - m_tailIndex))
            {
                m_tailIndex += (int)count;
                return;
            }

            m_buffers.RemoveAt(0);
            count -= (BUFFER_SIZE - m_tailIndex);
            m_tailIndex = 0;

            while (count >= BUFFER_SIZE)
            {
                m_buffers.RemoveAt(0);
                count -= BUFFER_SIZE;
            }

            m_tailIndex += (int)count;
            if (m_buffers.Count == 0)
            {
                m_headIndex = 0;
                m_tailIndex = 0;
                m_buffers.Add(new byte[BUFFER_SIZE]);
            }

            if (m_buffers.Count <= 1 && m_tailIndex > m_headIndex)
                throw new Exception(Strings.RollingBuffer.InternalError);
        }

        /// <summary>
        /// Returns the byte at the given position.
        /// </summary>
        /// <param name="index">The index of the byte</param>
        /// <returns>The byte value</returns>
        public byte GetByteAt(long index)
        {
            int ix = 0;
            if (index > this.Count - 1)
                throw new Exception(Strings.RollingBuffer.BufferExhaustedError);
            if (index < 0)
                throw new ArgumentOutOfRangeException(Strings.RollingBuffer.NegativeIndexError);

            while (GetAvalible(ix) <= index)
            {
                index -= GetAvalible(ix);
                ix++;
            }

            long offset = ix == 0 ? m_tailIndex : 0;
            offset += index;
            return m_buffers[ix][offset];
        }
    }
}
