using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Core
{
    /// <summary>
    /// This represents a temporary folder, that will be automatically removed when disposed
    /// </summary>
    public class TempFolder : IDisposable
    {
        private string m_folder;

        public TempFolder()
            : this(System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()))
        {
        }

        public TempFolder(string folder)
        {
            m_folder = folder;
            if (!m_folder.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                m_folder += System.IO.Path.DirectorySeparatorChar;
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
    }
}
