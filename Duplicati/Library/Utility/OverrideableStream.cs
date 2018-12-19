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

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This class is a wrapper for a stream.
    /// It's only purpose is to free other wrappers from implementing the boilerplate functions,
    /// and allow the derived classes to override single functions.
    /// </summary>
    public class OverrideableStream : Stream
    {
        protected System.IO.Stream m_basestream;

        public OverrideableStream(Stream basestream)
        {
            if (basestream == null)
                throw new ArgumentNullException(nameof(basestream));
            m_basestream = basestream;
        }

        public override bool CanRead
        {
            get { return m_basestream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return m_basestream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return m_basestream.CanWrite; }
        }

        public override void Flush()
        {
            m_basestream.Flush();
        }

        public override long Length
        {
            get { return m_basestream.Length; }
        }

        public override long Position
        {
            get
            {
                return m_basestream.Position;
            }
            set
            {
                m_basestream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_basestream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            return m_basestream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            m_basestream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_basestream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (m_basestream != null)
                m_basestream.Dispose();
            m_basestream = null;
            base.Dispose(disposing);
        }
        
    }
}
