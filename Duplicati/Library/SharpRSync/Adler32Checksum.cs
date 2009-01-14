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

        public Adler32Checksum(RollingBuffer buffer, int blocksize)
        {
            m_buffer = buffer;
            m_done = false;
            m_count = 0;
            m_blockSize = blocksize;
            Reset();
        }
        
        public Adler32Checksum(System.IO.Stream source, int blocksize)
        {
            m_source = source;
            m_done = false;
            m_count = 0;
            m_blockSize = blocksize;
            Reset();
        }

        public Adler32Checksum(System.IO.Stream source)
            : this(source, DEFAULT_BLOCK_SIZE)
        {
        }

        public bool Done { get { return m_done; } }
        public uint Checksum { get { return (uint)((m_s1 & 0xffff) + (m_s2 << 16)); } }

        public void Reset()
        {
            int i;
            byte[] buf = new byte[4];
            int a;

            m_s1 = m_s2 = 0;

            for (i = 0; i < m_blockSize - (m_blockSize % 4); i += 4)
            {
                if (m_source != null)
                    a = Utility.ForceStreamRead(m_source, buf, buf.Length);
                else
                {
                    a = m_buffer.Advance(4);
                    m_buffer.GetHead(buf, 0, 4);
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
                    return;
                }
            }


            a = Utility.ForceStreamRead(m_source, buf, (int)m_blockSize % 4);
            for (i = 0; i < a; i++)
                UpdateChecksum(buf[i]);
            m_count += a;

        }

        public bool AdvanceChecksum()
        {
            if (m_done)
                return false;

            int b;
            if (m_source != null)
                b = m_source.ReadByte();
            else
            {
                if (m_buffer.Advance(1) != 1)
                {
                    m_done = true;
                    return false;
                }
                b = m_buffer.GetHead(1)[0];
            }

            if (b == -1)
            {
                m_done = true;
                return false;
            }

            UpdateChecksum((byte)b);
            return true;
        }

        private void UpdateChecksum(byte value)
        {
            m_s1 += value + CHAR_OFFSET;
            m_s2 += m_s1;

            m_s1 %= ushort.MaxValue + 1;
            m_s2 %= ushort.MaxValue + 1;
        }

    }
}
