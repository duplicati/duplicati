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

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// This class wraps a regular .Net stream for use with SharpSSH
    /// </summary>
    internal class OutputStreamWrapper: Tamir.SharpSsh.java.io.OutputStream
    {
        private System.IO.Stream m_stream;
        public OutputStreamWrapper(System.IO.Stream s)
        {
            m_stream = s;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override bool CanRead
        {
            get
            {
                return m_stream.CanRead;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return m_stream.CanSeek;
            }
        }

        public override bool CanTimeout
        {
            get
            {
                return m_stream.CanTimeout;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return m_stream.CanWrite;
            }
        }

        public override void close()
        {
            base.close();
            m_stream.Close();
        }

        public override void Close()
        {
            base.Close();
            m_stream.Close();
        }

        public override void Flush()
        {
            base.Flush();
            m_stream.Flush();
        }

        public override void flush()
        {
            base.flush();
            m_stream.Flush();
        }

        public override long Length
        {
            get
            {
                return m_stream.Length;
            }
        }

        public override long Position
        {
            get
            {
                return m_stream.Position;
            }
            set
            {
                m_stream.Position = value;
            }
        }

        public override void WriteByte(byte value)
        {
            m_stream.WriteByte(value);
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            return m_stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            m_stream.SetLength(value);
        }

        public override void write(byte[] buffer, int offset, int count)
        {
            m_stream.Write(buffer, offset, count);
        }
    }
}
