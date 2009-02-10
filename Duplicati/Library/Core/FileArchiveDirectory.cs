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
