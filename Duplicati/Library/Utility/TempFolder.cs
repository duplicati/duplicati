// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using Duplicati.Library.Common.IO;

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
            m_folder = Util.AppendDirSeparator(folder);
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