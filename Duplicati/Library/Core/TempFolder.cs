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
    /// This represents a temporary folder, that will be automatically removed when disposed
    /// </summary>
    public class TempFolder : IDisposable
    {
        private string m_folder;

        public TempFolder()
            : this(System.IO.Path.Combine(SystemTempPath, System.IO.Path.GetRandomFileName()))
        {
        }

        public TempFolder(string folder)
        {
            m_folder = Core.Utility.AppendDirSeperator(folder);
            System.IO.Directory.CreateDirectory(m_folder);
        }

        public static implicit operator string(TempFolder folder)
        {
            return folder == null ? null : folder.m_folder;
        }

        public static implicit operator TempFolder(string folder)
        {
            return new TempFolder(folder);
        }

        #region IDisposable Members

        public void Dispose()
        {
            try
            {
                if (m_folder != null && System.IO.Directory.Exists(m_folder))
                    System.IO.Directory.Delete(m_folder, true);
                m_folder = null;
            }
            catch 
            {
            }
        }

        #endregion

        private static string m_system_temp_dir = null;

        /// <summary>
        /// Gets or sets the global temporary path used to store temporary files.
        /// Set to null to use the system default.
        /// </summary>
        public static string SystemTempPath
        {
            get { return m_system_temp_dir == null ? System.IO.Path.GetTempPath() : m_system_temp_dir; }
            set 
            {
                if (!System.IO.Directory.Exists(value))
                    throw new Exception("Temporary folder does not exist: " + value);
                m_system_temp_dir = value; 
            }
        }
    }
}
