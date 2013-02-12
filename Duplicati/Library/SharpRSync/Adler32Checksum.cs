#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
        /// The charated offset used in the checksum
        /// </summary>
        private const ushort CHAR_OFFSET = 31;

        /// <summary>
        /// Calculates the Adler32 checksum for the given data
        /// </summary>
        /// <param name="data">The data to calculate the checksum for</param>
        /// <param name="offset">The offset into the data to start reading</param>
        /// <param name="count">The number of bytes to process</param>
        /// <returns>The adler32 checksum for the data</returns>
        public static uint Calculate(byte[] data, int offset, int count)
        {
            int i;

            ushort s1 = 0;
            ushort s2 = 0;

            int leftoverdata = (count % 4);
            long rounds = count - leftoverdata;

            //First do an optimized processing in chunks of 4 bytes
            for (i = 0; i < rounds; i += 4)
            {
                s2 += (ushort)(
                    4 * (s1 + data[offset + 0]) + 
                    3 * data[offset + 1] +
                    2 * data[offset + 2] + 
                    data[offset + 3] + 
                    10 * CHAR_OFFSET);

                s1 += (ushort)(
                    data[offset + 0] + 
                    data[offset + 1] + 
                    data[offset + 2] + 
                    data[offset + 3] +
                        4 * CHAR_OFFSET);

                offset += 4;
            }

            //Do a single step update of the remaining bytes
            for (i = 0; i < leftoverdata; i++)
            {
                s1 += (ushort)(data[offset + i] + CHAR_OFFSET);
                s2 += s1;
            }

            return (uint)((s1 & 0xffff) + (s2 << 16));
        }

        /// <summary>
        /// Updates an adler32 cheksum by excluding a byte, and including another
        /// </summary>
        /// <param name="out_byte">The byte that is no longer in the checksum</param>
        /// <param name="in_byte">The byte that is added to the checksum</param>
        /// <param name="checksum">The checksum including the out_byte</param>
        /// <param name="bytecount">The number of bytes the checksum contains</param>
        /// <returns>An updated checksum</returns>
        public static uint Roll(byte out_byte, byte in_byte, uint checksum, long bytecount)
        {
            ushort s1 = (ushort)(checksum & 0xffff);
            ushort s2 = (ushort)(checksum >> 16);

            s1 += (ushort)(in_byte - out_byte);
            s2 += (ushort)(s1 - (bytecount * (out_byte + CHAR_OFFSET)));

            return (uint)((s1 & 0xffff) + (s2 << 16));
        }
    }
}
