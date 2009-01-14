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

namespace Duplicati.Backend
{
    public class File : IBackendInterface
    {
        #region IBackendInterface Members

        public string DisplayName
        {
            get { return "File based"; }
        }

        public string ProtocolKey
        {
            get { return "file"; }
        }

        public List<FileEntry> List(string url, Dictionary<string, string> options)
        {
            Uri u = new Uri(url);
            string path = u.LocalPath;
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

        public void Put(string url, Dictionary<string, string> options, System.IO.Stream stream)
        {
            Uri u = new Uri(url);
            string path = u.LocalPath;
            using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Utility.CopyStream(stream, fs, true);
        }

        public System.IO.Stream Get(string url, Dictionary<string, string> options)
        {
            Uri u = new Uri(url);
            string path = u.LocalPath;
            return new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None);
        }

        public void Delete(string url, Dictionary<string, string> options)
        {
            Uri u = new Uri(url);
            string path = u.LocalPath;
            System.IO.File.Delete(path);
        }

        #endregion
    }
}
