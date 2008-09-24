#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.SharpRSync
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
            m_headIndex = 0;
            m_tailIndex = 0;
        }

        public int Count { get { return m_buffers.Count * BUFFER_SIZE - m_tailIndex - (BUFFER_SIZE - m_headIndex); } }

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
        public byte[] GetHead(int count)
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
        public void GetHead(byte[] buf, int offset, int count)
        {
            int ix = 0;
            int total = this.Count;
            if (total < count)
                throw new Exception("Buffer has too few bytes");
            
            int b = m_tailIndex;

            while (total > count)
            {
                int availible = GetAvalible(ix);
                int diff = Math.Min(availible, total - count);
                total -= diff;
                availible -= diff;
                b += diff;

                if (availible == 0)
                {
                    ix++;
                    b = 0;
                }
            }

            while (count > 0)
            {
                int avalible = Math.Min(GetAvalible(ix), count);
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
        public byte[] GetTail(int count)
        {
            byte[] tmp = new byte[BUFFER_SIZE];
            GetTail(tmp, 0, count);
            return tmp;
        }

        /// <summary>
        /// Returns the leading bytes
        /// </summary>
        /// <param name="count">The number of bytes to return</param>
        /// <param name="buf">The buffer to write the bytes into</param>
        /// <param name="offset">The offset into the buffer</param>
        public void GetTail(byte[] buf, int offset, int count)
        {
            int ix = 0;
            if (this.Count < count)
                throw new Exception("Buffer has too few bytes");

            int b = m_tailIndex;
            while (count > 0)
            {
                int avalible = Math.Min(GetAvalible(ix), count);
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
        public void DropTail(int count)
        {
            if (count < (BUFFER_SIZE - m_tailIndex))
            {
                m_tailIndex += count;
                return;
            }

            m_buffers.RemoveAt(0);
            count -= (BUFFER_SIZE - m_tailIndex);
            m_tailIndex = 0;

            while (count > BUFFER_SIZE)
            {
                m_buffers.RemoveAt(0);
                count -= BUFFER_SIZE;
            }

            m_tailIndex += count;
        }
    }
}
