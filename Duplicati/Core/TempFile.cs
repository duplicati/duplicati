using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Core
{
    /// <summary>
    /// This class represents a temporary file that will be automatically deleted when disposed
    /// </summary>
    public class TempFile : IDisposable 
    {
        private string m_path;

        public TempFile()
            : this(System.IO.Path.GetTempFileName())
        {
        }

        public TempFile(string path)
        {
            m_path = path;
            if (!System.IO.File.Exists(m_path))
                System.IO.File.Create(m_path);
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
