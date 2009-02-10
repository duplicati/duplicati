using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Library.Encryption
{
    internal class CryptoStreamWrapper : Core.OverrideableStream
    {
        private bool m_hasFlushed = false;

        /// <summary>
        /// Wraps a crypto stream, ensuring that it is correctly disposed
        /// </summary>
        /// <param name="basestream">The stream to wrape</param>
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
