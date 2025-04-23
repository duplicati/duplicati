// Copyright (C) 2025, The Duplicati Team
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
                throw new InvalidOperationException(Strings.HashCalculatingStream.IncorrectUsageError);
            m_hasRead = true;

            int tmp = base.Read(buffer, offset, count);
            UpdateHash(buffer, offset, tmp);
            return tmp;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (m_hasRead)
                throw new InvalidOperationException(Strings.HashCalculatingStream.IncorrectUsageError);
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
