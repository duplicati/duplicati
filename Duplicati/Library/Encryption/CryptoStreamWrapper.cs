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


namespace Duplicati.Library.Encryption
{
    internal class CryptoStreamWrapper : Utility.OverrideableStream
    {
        private bool m_hasFlushed = false;

        /// <summary>
        /// Wraps a crypto stream, ensuring that it is correctly disposed
        /// </summary>
        /// <param name="basestream">The stream to wrap</param>
        public CryptoStreamWrapper(System.Security.Cryptography.CryptoStream basestream)
            : base(basestream)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (!m_hasFlushed && m_basestream.CanWrite)
            {
                ((System.Security.Cryptography.CryptoStream)m_basestream).FlushFinalBlock();
                m_hasFlushed = true;
            }

            base.Dispose(disposing);
        }
    }
}
