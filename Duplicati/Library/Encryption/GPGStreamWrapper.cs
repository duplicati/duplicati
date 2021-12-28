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
using System.IO;

namespace Duplicati.Library.Encryption
{
    internal class GPGStreamWrapper : Utility.OverrideableStream
    {
        private System.Diagnostics.Process m_p;
        private System.Threading.Thread m_t;

        /// <summary>
        /// Wraps a crypto stream, ensuring that it is correctly disposed
        /// </summary>
        /// <param name="basestream">The stream to wrap</param>
        public GPGStreamWrapper(System.Diagnostics.Process p, System.Threading.Thread t, Stream basestream)
            : base(basestream)
        {
            if (p == null)
                throw new NullReferenceException("p");
            if (t == null)
                throw new NullReferenceException("t");
            
            m_p = p;
            m_t = t;
        }

        protected override void Dispose(bool disposing)
        {
            if (m_p != null)
            {
                m_basestream.Close();

                if (!m_t.Join(5000))
                    throw new System.Security.Cryptography.CryptographicException(Strings.GPGStreamWrapper.GPGFlushError);

                if (!m_p.WaitForExit(5000))
                    throw new System.Security.Cryptography.CryptographicException(Strings.GPGStreamWrapper.GPGTerminateError);

                if (!m_p.StandardError.EndOfStream)
                {
                    string errmsg = m_p.StandardError.ReadToEnd();
                    if (errmsg.Contains("decryption failed:"))
                        throw new System.Security.Cryptography.CryptographicException(Strings.GPGStreamWrapper.DecryptionError(errmsg));
                }

                m_p.Dispose();
                m_p = null;

                m_t = null;
            }

            base.Dispose(disposing);
        }
    }
}
