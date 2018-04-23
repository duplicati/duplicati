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

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This class represents a temporary file that will be automatically deleted when disposed
    /// </summary>
    public class TempFile : IDisposable 
    {
        /// <summary>
        /// The prefix applied to all temporary files
        /// </summary>
        public static string APPLICATION_PREFIX = Utility.GetEntryAssembly().FullName.Substring(0, 3).ToLower() + "-";
        
        private string m_path;
        private bool m_protect;

#if DEBUG
        //In debug mode, we track the creation of temporary files, and encode the generating method into the name
        private static readonly object m_lock = new object();
        private static Dictionary<string, System.Diagnostics.StackTrace> m_fileTrace = new Dictionary<string, System.Diagnostics.StackTrace>();
        
        public static System.Diagnostics.StackTrace GetStackTraceForTempFile(string filename)
        {
            lock(m_lock)
                if (m_fileTrace.ContainsKey(filename))
                    return m_fileTrace[filename];
                else
                    return null;
        }
        
        private static string GenerateUniqueName()
        {
            var st = new System.Diagnostics.StackTrace();
            foreach(var f in st.GetFrames())
                if (f.GetMethod().DeclaringType.Assembly != typeof(TempFile).Assembly)
                {
                    var n = string.Format("{0}_{1}_{2}_{3}", f.GetMethod().DeclaringType.FullName, f.GetMethod().Name, Library.Utility.Utility.SerializeDateTime(DateTime.UtcNow), Guid.NewGuid().ToString().Substring(0, 8));
                    if (n.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                        n = string.Format("{0}_{1}_{2}_{3}", f.GetMethod().DeclaringType.Name, f.GetMethod().Name, Library.Utility.Utility.SerializeDateTime(DateTime.UtcNow), Guid.NewGuid().ToString().Substring(0, 8));
                    if (n.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0)
                    {
                        lock(m_lock)
                            m_fileTrace.Add(n, st);
                        return n;
                    }
                }
                
            var s = Guid.NewGuid().ToString();
            lock(m_lock)
                m_fileTrace.Add(s, st);
            return s;            
        }
#else
        private static string GenerateUniqueName()
        {
            return APPLICATION_PREFIX + Guid.NewGuid().ToString();
        }
#endif
        /// <summary>
        /// The name of the temp file
        /// </summary>
        public string Name => m_path;

        /// <summary>
        /// Gets all temporary files found in the current tempdir, that matches the application prefix
        /// </summary>
        /// <returns>The application temp files.</returns>
        private static IEnumerable<string> GetApplicationTempFiles()
        {
            return System.IO.Directory.GetFiles(TempFolder.SystemTempPath, APPLICATION_PREFIX + "*");
        }
        
        /// <summary>
        /// Attempts to delete all temporary files for this application
        /// </summary>
        public static void RemoveAllApplicationTempFiles()
        {
            foreach(var s in GetApplicationTempFiles())
                try { System.IO.File.Delete(s); }
                catch { }
        }

        /// <summary>
        /// Removes all old temporary files for this application.
        /// </summary>
        /// <param name="errorcallback">An optional callback method for logging errors</param>
        public static void RemoveOldApplicationTempFiles(Action<string, Exception> errorcallback = null)
        {
            var expires = TimeSpan.FromDays(30);
            foreach(var e in GetApplicationTempFiles())
                try
                {
                    if (DateTime.Now > (System.IO.File.GetLastWriteTimeUtc(e) + expires))
                        System.IO.File.Delete(e);
                }
                catch (Exception ex)
                {
                    if (errorcallback != null)
                        errorcallback(e, ex);
                }
        }
        
        public TempFile()
            : this(System.IO.Path.Combine(TempFolder.SystemTempPath, GenerateUniqueName()))
        {
        }

        private TempFile(string path)
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
        
        public static TempFile WrapExistingFile(string path)
        {
            return new TempFile(path);
        }

        public static TempFile CreateInFolder(string path, bool autocreatefolder)
        {
            if (autocreatefolder && !System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);

            return new TempFile(System.IO.Path.Combine(path, GenerateUniqueName()));
        }

        public static TempFile CreateWithPrefix(string prefix)
        {
            return new TempFile(System.IO.Path.Combine(TempFolder.SystemTempPath, prefix + GenerateUniqueName()));
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
                GC.SuppressFinalize(this);
                
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
        
        #region IDisposable Members

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
        
        ~TempFile()
        {
            Dispose(false);
        }

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
