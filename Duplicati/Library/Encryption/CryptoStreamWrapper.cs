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
using System.Text;
using System.IO;

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
