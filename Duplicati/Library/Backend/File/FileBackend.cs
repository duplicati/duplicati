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

namespace Duplicati.Library.Backend
{
    public class File : IBackendInterface
    {
        private string m_path;
        Dictionary<string, string> m_options;

        public File()
        {
        }

        public File(string url, Dictionary<string, string> options)
        {
            m_options = options;
            m_path = ExtractFilename(url, m_options);
            if (!System.IO.Path.IsPathRooted(url))
                m_path = System.IO.Path.GetFullPath(m_path);
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get { return "File based"; }
        }

        public string ProtocolKey
        {
            get { return "file"; }
        }

        public List<FileEntry> List()
        {
            string path = m_path;
            List<FileEntry> ls = new List<FileEntry>();

            //TODO: Impersonate, if username+password is applied

            foreach (string s in System.IO.Directory.GetFiles(path))
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(s);
                ls.Add(new FileEntry(fi.Name, fi.Length, fi.LastAccessTime, fi.LastWriteTime));
            }

            foreach (string s in System.IO.Directory.GetDirectories(path))
            {
                System.IO.DirectoryInfo di = new System.IO.DirectoryInfo(s);
                FileEntry fe = new FileEntry(di.Name,0, di.LastAccessTime, di.LastWriteTime);
                fe.IsFolder = true;
                ls.Add(fe);
            }

            return ls;
        }

        public void Put(string remotename, string filename)
        {
            string path = System.IO.Path.Combine(m_path, remotename);
            System.IO.File.Copy(filename, path, true);
        }

        public void Get(string remotename, string filename)
        {
            string path = System.IO.Path.Combine(m_path, remotename);
            System.IO.File.Copy(path, filename, true);
        }

        public void Delete(string remotename)
        {
            string path = System.IO.Path.Combine(m_path, remotename);
            System.IO.File.Delete(path);
        }

        private string ExtractFilename(string url, Dictionary<string, string> options)
        {
            //TODO: Read out username/password
            return url.Substring("file://".Length);
        }

        #endregion
    }
}
