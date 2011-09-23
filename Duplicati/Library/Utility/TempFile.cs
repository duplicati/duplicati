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

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This class represents a temporary file that will be automatically deleted when disposed
    /// </summary>
    public class TempFile : IDisposable 
    {
        private string m_path;
        private bool m_protect;

        public TempFile()
            : this(System.IO.Path.Combine(TempFolder.SystemTempPath, Guid.NewGuid().ToString()))
        {
        }

        public TempFile(string path)
        {
            m_path = path;
            m_protect = false;
            if (!System.IO.File.Exists(m_path))
                using (System.IO.File.Create(m_path))
                { /*Dispose it immediately*/ } 
        }

        /// <summary>
        /// A value indicating if the file is protected, meaning that it will not be deleted when the instance is disposed.
        /// Defaults to false, meaning that the file will be deleted when disposed.
        /// </summary>
        public bool Protected
        {
            get { return m_protect; }
            set { m_protect = value; }
        }

        public static implicit operator string(TempFile path)
        {
            return path == null ? null : path.m_path;
        }

        public static implicit operator TempFile(string path)
        {
            return new TempFile(path);
        }

        #region IDisposable Members

        public void Dispose()
        {
            try
            {
                if (!m_protect && m_path != null && System.IO.File.Exists(m_path))
                    System.IO.File.Delete(m_path);
                m_path = null;
            }
            catch
            {
            }
        }

        #endregion

        /// <summary>
        /// Swaps two instances of temporary files, equivalent to renaming the files but requires no IO
        /// </summary>
        /// <param name="tf">The temp file to swap with</param>
        public void Swap(TempFile tf)
        {
            string p = m_path;
            m_path = tf.m_path;
            tf.m_path = p;
        }
    }
}
