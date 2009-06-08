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

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// A special stream that does on-the-fly MD5 calculations
    /// </summary>
    class MD5CalculatingStream : Core.OverrideableStream
    {
        private System.Security.Cryptography.MD5 m_hash;
        private byte[] m_finalHash = null;

        bool m_hasRead = false;
        bool m_hasWritten = false;

        public MD5CalculatingStream(System.IO.Stream basestream)
            : base(basestream)
        {
            m_hash = (System.Security.Cryptography.MD5)System.Security.Cryptography.HashAlgorithm.Create("MD5");
            m_hash.Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (m_hash != null)
            {
                m_hash.Clear();
                m_hash = null;
            }
        }
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_hasWritten)
                throw new InvalidOperationException("Cannot read and write on the same stream");
            m_hasRead = true;


            //TODO: This needs to use a while loop, and a storage of prev. data
            m_hash.TransformBlock(buffer, offset, count, buffer, offset);
            return base.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (m_hasRead)
                throw new InvalidOperationException("Cannot read and write on the same stream");
            m_hasWritten = true;

            //TODO: This needs to use a while loop, and a storage of prev. data
            m_hash.TransformBlock(buffer, offset, count, buffer, offset);
            base.Write(buffer, offset, count);
        }

        public string GetFinalHashString()
        {
            return Core.Utility.ByteArrayAsHexString(this.GetFinalHash());
        }

        public byte[] GetFinalHash()
        {
            if (m_finalHash == null)
            {
                m_hash.TransformFinalBlock(new byte[0], 0, 0);
                m_finalHash = m_hash.Hash;
            }
            return m_finalHash;
        }
    }
}
