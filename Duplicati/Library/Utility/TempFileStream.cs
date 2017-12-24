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
    /// Represents a filestream for a temporary file, which will be deleted when disposed
    /// </summary>
    public class TempFileStream : Stream
    {
        private Stream m_stream;
        private TempFile m_file;

        public TempFileStream()
            : this(new TempFile())
        {
        }

        /// <summary>
        /// Full name of the temp file.
        /// </summary>
        public string Name => m_file?.Name;

        public TempFileStream(string file)
            : this(TempFile.WrapExistingFile(file))
        {
        }

        public TempFileStream(TempFile file)
        {
            m_file = file ?? throw new ArgumentNullException(nameof(file));
            m_stream = System.IO.File.Open(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        }

        public override bool CanRead
        {
            get { return m_stream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return m_stream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return m_stream.CanWrite; }
        }

        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Length
        {
            get { return m_stream.Length; }
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return m_stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            m_stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_stream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (m_stream != null)
                {
                    m_stream.Dispose();
                    m_stream = null;
                }
                if (m_file != null)
                {
                    m_file.Dispose();
                    m_file = null;
                }
            }
        }
    }
}
