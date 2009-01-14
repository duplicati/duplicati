#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.Library.Core
{
    /// <summary>
    /// This class represents a temporary file that will be automatically deleted when disposed
    /// </summary>
    public class TempFile : IDisposable 
    {
        private string m_path;

        public TempFile()
            : this(System.IO.Path.Combine(TempFolder.SystemTempPath, Guid.NewGuid().ToString()))
        {
        }

        public TempFile(string path)
        {
            m_path = path;
            if (!System.IO.File.Exists(m_path))
                using (System.IO.File.Create(m_path))
                { /*Dispose it immediately*/ } 
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
                if (m_path != null && System.IO.File.Exists(m_path))
                    System.IO.File.Delete(m_path);
                m_path = null;
            }
            catch
            {
            }
        }

        #endregion
    }
}
