#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
    /// This is an implementation of the Adler-32 rolling checksum.
    /// This implementation is converted to C# from C code in librsync.
    /// </summary>
    public class Adler32Checksum
    {
        /// <summary>
        /// The number of bytes to include in each checksum
        /// </summary>
        public const int DEFAULT_BLOCK_SIZE = 2048;
        private const uint CHAR_OFFSET = 31;

        private System.IO.Stream m_source;
        private RollingBuffer m_buffer;
        private bool m_done;

        private uint m_s1;
        private uint m_s2;
        private long m_count;
        private long m_blockSize;

        /// <summary>
        /// Constructs a new adler32 instance, reading from a rolling buffer
        /// </summary>
        /// <param name="buffer">The buffer to read from</param>
        /// <param name="blocksize">The size of a single block</param>
        public Adler32Checksum(RollingBuffer buffer, int blocksize)
        {
            m_buffer = buffer;
            m_done = false;
            m_count = 0;
            m_blockSize = blocksize;
            Reset();
        }

        /// <summary>
        /// Constructs a new adler32 instance, reading from a stream
        /// </summary>
        /// <param name="buffer">The buffer to read from</param>
        /// <param name="blocksize">The size of a single block</param>
        public Adler32Checksum(System.IO.Stream source, int blocksize)
        {
            m_source = source;
            m_done = false;
            m_count = 0;
            m_blockSize = blocksize;
            Reset();
        }

        /// <summary>
        /// Constructs a new adler32 instance, reading from a stream
        /// </summary>
        /// <param name="sourc">The buffer to read from</param>
        public Adler32Checksum(System.IO.Stream source)
            : this(source, DEFAULT_BLOCK_SIZE)
        {
        }

        /// <summary>
        /// Gets a value indicating if the last data has been read from the source
        /// </summary>
        public bool Done { get { return m_done; } }
        
        /// <summary>
        /// Gets the current checksum
        /// </summary>
        public uint Checksum { get { return (uint)((m_s1 & 0xffff) + (m_s2 << 16)); } }

        /// <summary>
        /// Resets the checksum and calculates the checksum for a new block
        /// </summary>
        /// <returns>False if there were no new data</returns>
        public bool Reset()
        {
            int i;
            byte[] buf = new byte[4];
            int a;

            if (m_done)
                return false;

            m_s1 = m_s2 = 0;
            m_count = 0;

            for (i = 0; i < m_blockSize - (m_blockSize % 4); i += 4)
            {
                if (m_source != null)
                    a = Utility.ForceStreamRead(m_source, buf, buf.Length);
                else
                {
                    a = m_buffer.Advance(4);
                    m_buffer.GetHead(buf, 0, a);
                }

                if (a == 4)
                {
                    m_s2 += (uint)(4 * (m_s1 + buf[0]) + 3 * buf[1] +
                        2 * buf[2] + buf[3] + 10 * CHAR_OFFSET);
                    m_s1 += (uint)(buf[0] + buf[1] + buf[2] + buf[3] +
                        4 * CHAR_OFFSET);

                    m_s1 %= ushort.MaxValue + 1;
                    m_s2 %= ushort.MaxValue + 1;

                    m_count += 4;
                }
                else
                {
                    for(int j = 0; j < a; j++)
                        UpdateChecksum(buf[j]);
                    m_count += a;
                    m_done = true;
                    return m_count != 0;
                }
            }

            if (m_source != null)
                a = Utility.ForceStreamRead(m_source, buf, (int)m_blockSize % 4);
            else
            {
                a = m_buffer.Advance((int)m_blockSize % 4);
                m_buffer.GetHead(buf, 0, a);
            }

            for (i = 0; i < a; i++)
                UpdateChecksum(buf[i]);
            m_count += a;
            return true;
        }

        /// <summary>
        /// Rolls the checksum a number of bytes
        /// </summary>
        /// <param name="count">The number of bytes to roll</param>
        /// <returns>True if there is new data, false otherwise</returns>
        public bool AdvanceChecksum(int count)
        {
            if (m_done)
                return false;

            for (int i = 0; i < count; i++)
                if (!AdvanceChecksum())
                    break;

            return true;
        }

        /// <summary>
        /// Rolls the checksum a single byte
        /// </summary>
        /// <returns>True if a new byte was checksummed, false if there was no more data</returns>
        public bool AdvanceChecksum()
        {
            if (m_done)
                return false;

            int b;
            if (m_source != null)
            {
                b = m_source.ReadByte();
                if (b == -1)
                {
                    m_done = true;
                    return false;
                }
            }
            else
            {
                if (m_buffer.Advance(1) != 1)
                {
                    m_done = true;
                    return false;
                }
                b = m_buffer.GetByteAt(m_buffer.Count - 1);
            }

            m_count += 1;

            UpdateChecksum((byte)b);
            return true;
        }

        /// <summary>
        /// Updates the checksum with the read byte
        /// </summary>
        /// <param name="value">The byte read</param>
        private void UpdateChecksum(byte value)
        {
            m_s1 += value + CHAR_OFFSET;
            m_s2 += m_s1;

            m_s1 %= ushort.MaxValue + 1;
            m_s2 %= ushort.MaxValue + 1;
        }


        /// <summary>
        /// Rolls the buffer, so the checksum now fits a block that is shifted one byte from the previous one.
        /// </summary>
        /// <returns>True if new data was included, false otherwise</returns>
        public bool Rollbuffer()
        {
            if (m_buffer == null)
                throw new Exception("Cannot roll buffer, when the source is not a rolling buffer instance");
            
            if (m_done)
                return false;

            if (m_buffer.Advance(1) != 1)
            {
                m_done = true;
                return false;
            }

            byte out_byte = m_buffer.GetByteAt(m_buffer.Count - m_count - 1);
            byte in_byte = m_buffer.GetByteAt(m_buffer.Count - 1);

            //TODO: This can be done much nicer, but the .Net overflow detection
            //prevents it.

            /*
             * m_s1 += in - out; 
             * m_s2 += m_s1 - (m_count *(out + CHAR_OFFSET));
             */

            int diff = in_byte - out_byte;
            if (diff < 0)
                diff += ushort.MaxValue + 1;
            
            m_s1 = (uint)((diff + m_s1) % (ushort.MaxValue + 1));
            
            diff = (int)((m_count * (out_byte + CHAR_OFFSET)) % (ushort.MaxValue + 1));
            diff = (int)m_s1 - diff;
            if (diff < 0)
                diff += ushort.MaxValue + 1;

            m_s2 = (uint)((m_s2 + diff) % (ushort.MaxValue + 1));

            return true;
        }

    }
}
