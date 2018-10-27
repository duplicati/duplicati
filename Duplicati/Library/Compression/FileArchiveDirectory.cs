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
using System.Linq;


#endregion
using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Interface;
using Duplicati.Library.IO;

namespace Duplicati.Library.Compression
{
    /// <summary>
    /// This class exposes a local directory as a file archive.
    /// Used only internally for debugging, cannot be used as a storage method.
    /// </summary>
    public class FileArchiveDirectory : ICompression
    {
        string m_folder;

        /// <summary>
        /// Constructs a new FileArchive
        /// </summary>
        /// <param name="basefolder">The folder to base the archive on</param>
        public FileArchiveDirectory(string basefolder)
        {
            m_folder = Util.AppendDirSeparator(basefolder);
        }

        #region IFileArchive Members

        /// <summary>
        /// Unsupported filename extension property, throws a MissingMethodException
        /// </summary>
        public string FilenameExtension { get { throw new MissingMethodException(); } }
        /// <summary>
        /// Unsupported displayname property, throws a MissingMethodException
        /// </summary>
        public string DisplayName { get { throw new MissingMethodException(); } }
        /// <summary>
        /// Unsupported description property, throws a MissingMethodException
        /// </summary>
        public string Description { get { throw new MissingMethodException(); } }
        /// <summary>
        /// Unsupported supported commands property, throws a MissingMethodException
        /// </summary>
        public IList<ICommandLineArgument> SupportedCommands { get { throw new MissingMethodException(); } }

        /// <summary>
        /// Returns a list of files in the archive
        /// </summary>
        /// <param name="prefix">An optional prefix that is used to filter the list</param>
        /// <returns>A filtered list of filenames</returns>
        public string[] ListFiles(string prefix)
        {
            if (prefix == null) prefix = "";
            return Utility.Utility.EnumerateFiles(System.IO.Path.Combine(m_folder, prefix)).ToArray();
        }

        /// <summary>
        /// Returns a list of files in the archive
        /// </summary>
        /// <param name="prefix">An optional prefix that is used to filter the list</param>
        /// <returns>A filtered list of filenames</returns>
        public IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string prefix)
        {
            if (prefix == null) prefix = "";
            List<KeyValuePair<string, long>> res = new List<KeyValuePair<string,long>>();
            foreach(string s in Utility.Utility.EnumerateFiles(System.IO.Path.Combine(m_folder, prefix)))
                res.Add(new KeyValuePair<string, long>(s, new System.IO.FileInfo(s).Length));

            return res;

        }

        /// <summary>
        /// Opens the file for reading
        /// </summary>
        /// <param name="file">The name of the file</param>
        /// <returns>A stream with the file contents</returns>
        public System.IO.Stream OpenRead(string file)
        {
            return System.IO.File.OpenRead(System.IO.Path.Combine(m_folder, file));
        }

        /// <summary>
        /// Creates a new empty file
        /// </summary>
        /// <param name="file">The name of the file to create</param>
        /// <param name="hint">A hint to the compressor as to how compressible the file data is</param>
        /// <param name="lastWrite">The time the file was last written</param>
        /// <returns>The stream used to access the file</returns>
        public System.IO.Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
        {
            string path = System.IO.Path.Combine(m_folder, file);
            System.IO.Stream res = System.IO.File.Create(path);

            //TODO: This should actually be set when closing the stream
            System.IO.File.SetLastWriteTime(path, lastWrite);
            return res;
        }

        /// <summary>
        /// Returns a value that indicates if the file exists
        /// </summary>
        /// <param name="file">The name of the file to test existence for</param>
        /// <returns>True if the file exists, false otherwise</returns>
        public bool FileExists(string file)
        {
            return System.IO.File.Exists(System.IO.Path.Combine(m_folder, file));
        }

        /// <summary>
        /// Gets the current size of the archive
        /// </summary>
        public long Size
        {
            get { return Utility.Utility.GetDirectorySize(m_folder, null); }
        }

        /// <summary>
        /// Gets the last write time for a file
        /// </summary>
        /// <param name="file">The name of the file to query</param>
        /// <returns>The last write time for the file</returns>
        public DateTime GetLastWriteTime(string file)
        {
            return System.IO.File.GetLastWriteTime(System.IO.Path.Combine(m_folder, file));
        }

        /// <summary>
        /// The size of the current unflushed buffer
        /// </summary>
        public long FlushBufferSize { get { return 0; } }

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Disposes the instance
        /// </summary>
        public void Dispose()
        {
            m_folder = null;
        }

        #endregion
    }
}
