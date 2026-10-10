// Copyright (C) 2026, The Duplicati Team
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
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Implements the CRC-32C (Castagnoli) checksum as a <see cref="HashAlgorithm"/>.
    /// This is the checksum used by Google Cloud Storage for object integrity validation,
    /// and the result is returned in big-endian byte order as GCS expects.
    /// The hardware CRC32C instructions are used on x86 (SSE4.2) and ARM64 when available,
    /// with a table-based fallback otherwise.
    /// </summary>
    public sealed class Crc32C : HashAlgorithm
    {
        /// <summary>
        /// The reflected CRC-32C polynomial.
        /// </summary>
        private const uint POLYNOMIAL = 0x82F63B78u;

        /// <summary>
        /// The lookup table used by the software fallback.
        /// </summary>
        private static readonly uint[] TABLE = CreateTable();

        /// <summary>
        /// The running (inverted) checksum state.
        /// </summary>
        private uint m_state;

        /// <summary>
        /// Creates a new CRC-32C hasher.
        /// </summary>
        public Crc32C()
        {
            HashSizeValue = 32;
            Initialize();
        }

        /// <inheritdoc/>
        public override void Initialize()
        {
            m_state = 0xFFFFFFFFu;
        }

        /// <inheritdoc/>
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            m_state = Update(m_state, array.AsSpan(ibStart, cbSize));
        }

        /// <inheritdoc/>
        protected override void HashCore(ReadOnlySpan<byte> source)
        {
            m_state = Update(m_state, source);
        }

        /// <inheritdoc/>
        protected override byte[] HashFinal()
        {
            var crc = m_state ^ 0xFFFFFFFFu;
            return [(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc];
        }

        /// <inheritdoc/>
        protected override bool TryHashFinal(Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < 4)
            {
                bytesWritten = 0;
                return false;
            }

            var crc = m_state ^ 0xFFFFFFFFu;
            destination[0] = (byte)(crc >> 24);
            destination[1] = (byte)(crc >> 16);
            destination[2] = (byte)(crc >> 8);
            destination[3] = (byte)crc;
            bytesWritten = 4;
            return true;
        }

        /// <summary>
        /// Computes the CRC-32C checksum of the data.
        /// </summary>
        /// <param name="data">The data to checksum.</param>
        /// <returns>The checksum value.</returns>
        public static uint Compute(ReadOnlySpan<byte> data)
        {
            return Update(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;
        }

        /// <summary>
        /// Feeds data into the running checksum state.
        /// </summary>
        /// <param name="crc">The current (inverted) state.</param>
        /// <param name="data">The data to process.</param>
        /// <returns>The updated state.</returns>
        private static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            if (Sse42.IsSupported)
                return UpdateSse42(crc, data);
            if (Crc32.IsSupported)
                return UpdateArm(crc, data);
            return UpdateTable(crc, data);
        }

        /// <summary>
        /// Feeds data into the running checksum using the x86 SSE4.2 instructions.
        /// </summary>
        /// <param name="crc">The current (inverted) state.</param>
        /// <param name="data">The data to process.</param>
        /// <returns>The updated state.</returns>
        private static uint UpdateSse42(uint crc, ReadOnlySpan<byte> data)
        {
            var i = 0;
            if (Sse42.X64.IsSupported)
            {
                ulong crc64 = crc;
                for (; i + 8 <= data.Length; i += 8)
                    crc64 = Sse42.X64.Crc32(crc64, BitConverter.ToUInt64(data.Slice(i, 8)));
                crc = (uint)crc64;
            }

            for (; i + 4 <= data.Length; i += 4)
                crc = Sse42.Crc32(crc, BitConverter.ToUInt32(data.Slice(i, 4)));
            for (; i < data.Length; i++)
                crc = Sse42.Crc32(crc, data[i]);
            return crc;
        }

        /// <summary>
        /// Feeds data into the running checksum using the ARM CRC32 instructions.
        /// </summary>
        /// <param name="crc">The current (inverted) state.</param>
        /// <param name="data">The data to process.</param>
        /// <returns>The updated state.</returns>
        private static uint UpdateArm(uint crc, ReadOnlySpan<byte> data)
        {
            var i = 0;
            if (Crc32.Arm64.IsSupported)
            {
                for (; i + 8 <= data.Length; i += 8)
                    crc = Crc32.Arm64.ComputeCrc32C(crc, BitConverter.ToUInt64(data.Slice(i, 8)));
            }

            for (; i + 4 <= data.Length; i += 4)
                crc = Crc32.ComputeCrc32C(crc, BitConverter.ToUInt32(data.Slice(i, 4)));
            for (; i < data.Length; i++)
                crc = Crc32.ComputeCrc32C(crc, data[i]);
            return crc;
        }

        /// <summary>
        /// Computes the CRC-32C checksum using only the lookup table, bypassing the hardware instructions.
        /// Exposed so the fallback can be tested on hardware that would otherwise never use it.
        /// </summary>
        /// <param name="data">The data to checksum.</param>
        /// <returns>The checksum value.</returns>
        internal static uint ComputeWithTable(ReadOnlySpan<byte> data)
        {
            return UpdateTable(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;
        }

        /// <summary>
        /// Feeds data into the running checksum using the lookup table.
        /// </summary>
        /// <param name="crc">The current (inverted) state.</param>
        /// <param name="data">The data to process.</param>
        /// <returns>The updated state.</returns>
        private static uint UpdateTable(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                crc = TABLE[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        /// <summary>
        /// Builds the lookup table for the software fallback.
        /// </summary>
        /// <returns>The lookup table.</returns>
        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var crc = i;
                for (var j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ POLYNOMIAL : crc >> 1;
                table[i] = crc;
            }

            return table;
        }
    }
}
