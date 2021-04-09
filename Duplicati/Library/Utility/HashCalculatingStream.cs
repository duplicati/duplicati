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
using System.Security.Cryptography;
using System.Text;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// A special stream that does on-the-fly hash calculations
    /// </summary>
    public class HashCalculatingStream : OverrideableStream
    {
        private System.Security.Cryptography.HashAlgorithm m_hash;
        private byte[] m_finalHash = null;

        bool m_hasRead = false;
        bool m_hasWritten = false;

        private byte[] m_hashbuffer = null;
        private int m_hashbufferLength = 0;

        public HashCalculatingStream(System.IO.Stream basestream, System.Security.Cryptography.HashAlgorithm algorithm)
            : base(basestream)
        {
            m_hash = algorithm;
            m_hash.Initialize();
            m_hashbuffer = new byte[m_hash.InputBlockSize];
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (m_hash != null)
            {
                m_hash.Clear();
                m_hash = null;

                m_hashbuffer = null;
                m_hashbufferLength = 0;
            }
        }
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_hasWritten)
                throw new InvalidOperationException(Strings.MD5CalculatingStream.IncorrectUsageError);
            m_hasRead = true;

            int tmp = base.Read(buffer, offset, count);
            UpdateHash(buffer, offset, tmp);
            return tmp;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (m_hasRead)
                throw new InvalidOperationException(Strings.MD5CalculatingStream.IncorrectUsageError);
            m_hasWritten = true;

            UpdateHash(buffer, offset, count);

            base.Write(buffer, offset, count);
        }

        private void UpdateHash(byte[] buffer, int offset, int count)
        {
            if (m_finalHash != null)
                throw new Exception("Cannot read/write after hash is read");

            //If we have a fragment from the last block, fill up
            if (m_hashbufferLength > 0 && count + m_hashbufferLength > m_hashbuffer.Length)
            {
                int bytesToUse = m_hashbuffer.Length - m_hashbufferLength;
                Array.Copy(buffer, m_hashbuffer, bytesToUse);
                m_hash.TransformBlock(m_hashbuffer, 0, m_hashbuffer.Length, m_hashbuffer, 0);
                count -= bytesToUse;
                offset += bytesToUse;
                m_hashbufferLength = 0;
            }

            //Take full blocks directly
            int fullBlocks = count / m_hashbuffer.Length;
            if (fullBlocks > 0)
            {
                int bytesToUse = fullBlocks * m_hashbuffer.Length;
                m_hash.TransformBlock(buffer, offset, bytesToUse, buffer, offset);
                count -= bytesToUse;
                offset += bytesToUse;
            }

            //Keep trailing bytes
            if (count > 0)
            {
                Array.Copy(buffer, offset, m_hashbuffer, 0, count);
                m_hashbufferLength = count;
            }
        }

        public string GetFinalHashString()
        {
            return Utility.ByteArrayAsHexString(this.GetFinalHash());
        }

        public byte[] GetFinalHash()
        {
            if (m_finalHash == null)
            {
                m_hash.TransformFinalBlock(m_hashbuffer, 0, m_hashbufferLength);
                m_finalHash = m_hash.Hash;
            }
            return m_finalHash;
        }
    }
}
