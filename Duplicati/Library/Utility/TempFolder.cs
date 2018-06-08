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

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This represents a temporary folder, that will be automatically removed when disposed
    /// </summary>
    public class TempFolder : IDisposable
    {
        private string m_folder;
        private bool m_protect;

        public TempFolder()
            : this(System.IO.Path.Combine(SystemTempPath, Guid.NewGuid().ToString()))
        {
        }

        public TempFolder(string folder)
        {
            m_protect = false;
            m_folder = Utility.AppendDirSeparator(folder);
            System.IO.Directory.CreateDirectory(m_folder);
        }

        /// <summary>
        /// A value indicating if the folder is protected, meaning that it will not be deleted when the instance is disposed.
        /// Defaults to false, meaning that the folder will be deleted when disposed.
        /// </summary>
        public bool Protected
        {
            get { return m_protect; }
            set { m_protect = value; }
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
                if (!m_protect && m_folder != null && System.IO.Directory.Exists(m_folder))
                    System.IO.Directory.Delete(m_folder, true);
                m_folder = null;
            }
            catch
            {
            }
        }

        #endregion

        /// <summary>
        /// Gets or sets the global temporary path used to store temporary files.
        /// Set to null to use the system default.
        /// </summary>
        public static string SystemTempPath
        {
            get {
                return SystemContextSettings.Tempdir;
            }
            set
            {
                if (!System.IO.Directory.Exists(value))
                    throw new Exception(Strings.TempFolder.TempFolderDoesNotExistError(value));
                SystemContextSettings.Tempdir = value;
            }
        }
    }
}