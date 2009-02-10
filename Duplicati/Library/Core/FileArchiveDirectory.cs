#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
    public class FileArchiveDirectory : IFileArchive
    {
        string m_folder;

        public FileArchiveDirectory(string basefolder)
        {
            m_folder = Utility.AppendDirSeperator(basefolder);
        }

        #region IFileArchive Members

        public string[] ListFiles(string prefix)
        {
            if (prefix == null) prefix = "";
            return Utility.EnumerateFiles(System.IO.Path.Combine(m_folder, prefix)).ToArray();
        }

        public string[] ListDirectories(string prefix)
        {
            if (prefix == null) prefix = "";
            return Utility.EnumerateFolders(System.IO.Path.Combine(m_folder, prefix)).ToArray();
        }

        public string[] ListEntries(string prefix)
        {
            if (prefix == null) prefix = "";
            return Utility.EnumerateFileSystemEntries(System.IO.Path.Combine(m_folder, prefix)).ToArray();
        }

        public byte[] ReadAllBytes(string file)
        {
            return System.IO.File.ReadAllBytes(System.IO.Path.Combine(m_folder, file));
        }

        public string[] ReadAllLines(string file)
        {
            return System.IO.File.ReadAllLines(System.IO.Path.Combine(m_folder, file));
        }

        public System.IO.Stream OpenRead(string file)
        {
            return System.IO.File.OpenRead(System.IO.Path.Combine(m_folder, file));
        }

        public System.IO.Stream OpenWrite(string file)
        {
            return System.IO.File.OpenWrite(System.IO.Path.Combine(m_folder, file));
        }

        public void WriteAllBytes(string file, byte[] data)
        {
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(m_folder, file), data);
        }

        public void WriteAllLines(string file, string[] data)
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(m_folder, file), data);
        }

        public void DeleteFile(string file)
        {
            System.IO.File.Delete(System.IO.Path.Combine(m_folder, file));
        }

        public System.IO.Stream CreateFile(string file)
        {
            return System.IO.File.Create(System.IO.Path.Combine(m_folder, file));
        }

        public void DeleteDirectory(string file)
        {
            System.IO.Directory.Delete(System.IO.Path.Combine(m_folder, file), false);
        }

        public void AddDirectory(string file)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(m_folder, file));
        }

        public bool FileExists(string file)
        {
            return System.IO.File.Exists(System.IO.Path.Combine(m_folder, file));
        }

        public bool DirectoryExists(string file)
        {
            return System.IO.Directory.Exists(System.IO.Path.Combine(m_folder, file));
        }

        public long Size
        {
            get
            {
                //TODO: Much faster with a callback
                long size = 0;
                foreach (string s in Core.Utility.EnumerateFiles(m_folder))
                    size += new System.IO.FileInfo(s).Length;

                return size;
            }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            m_folder = null;
        }

        #endregion
    }
}
