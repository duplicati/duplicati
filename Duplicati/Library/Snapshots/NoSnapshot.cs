#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// A simple implementation of snapshots that only uses the regular System.IO operations, 
    /// used to simplify code that uses snapshots, but also handles a non-snapshot enabled systems
    /// </summary>
    public abstract class NoSnapshot : ISnapshotService
    {
        /// <summary>
        /// The list of all folders in the snapshot
        /// </summary>
        protected string[] m_sourcefolders;

        /// <summary>
        /// A frequently used char-as-string
        /// </summary>
        protected readonly string DIR_SEP = System.IO.Path.DirectorySeparatorChar.ToString();

        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="sourcepaths">The folders that are about to be backed up</param>
        public NoSnapshot(string[] folders)
            : this(folders, new Dictionary<string, string>())
        {
        }

        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="folders">The folders that are about to be backed up</param>
        /// <param name="options">A set of system options</param>
        public NoSnapshot(string[] folders, Dictionary<string, string> options)
        {
            m_sourcefolders = new string[folders.Length];
            for (int i = 0; i < m_sourcefolders.Length; i++)
                m_sourcefolders[i] = Utility.Utility.AppendDirSeparator(folders[i]);
        }

        #region Private Methods
        /// <summary>
        /// Lists all folders in the given folder
        /// </summary>
        /// <returns>All folders found in the folder</returns>
        /// <param name='folder'>The folder to examinate</param>
        protected virtual string[] ListFolders(string folder)
        {
            return System.IO.Directory.GetDirectories(folder);
        }


        /// <summary>
        /// Lists all files in the given folder
        /// </summary>
        /// <returns>All folders found in the folder</returns>
        /// <param name='folder'>The folder to examinate</param>
        protected virtual string[] ListFiles(string folder)
        {
            return System.IO.Directory.GetFiles(folder);
        }
        #endregion

        #region ISnapshotService Members

        /// <summary>
        /// Cleans up any resources and closes the backup set
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// Enumerates all files and folders in the snapshot
        /// </summary>
        /// <param name="startpath">The path from which to retrieve files and folders</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        public void EnumerateFilesAndFolders(string startpath, Duplicati.Library.Utility.Utility.EnumerationCallbackDelegate callback)
        {
            foreach (string s in m_sourcefolders)
                if (s.Equals(startpath, Utility.Utility.ClientFilenameStringComparision))
                {
                    Utility.Utility.EnumerateFileSystemEntries(s, callback, this.ListFolders, this.ListFiles, this.GetAttributes);
                    return;
                }

            throw new InvalidOperationException(string.Format(Strings.Shared.InvalidEnumPathError, startpath));
        }

        /// <summary>
        /// Enumerates all files and folders in the snapshot
        /// </summary>
        /// <param name="callback">The callback to invoke with each found path</param>
        public void EnumerateFilesAndFolders(Duplicati.Library.Utility.Utility.EnumerationCallbackDelegate callback)
        {
            foreach (string s in m_sourcefolders)
                Utility.Utility.EnumerateFileSystemEntries(s, callback, this.ListFolders, this.ListFiles, this.GetAttributes);
        }

        /// <summary>
        /// Gets the last write time of a given file
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public virtual DateTime GetLastWriteTime(string file)
        {
            return System.IO.File.GetLastWriteTime(file);
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public virtual System.IO.Stream OpenRead(string file)
        {
            return System.IO.File.OpenRead(file);
        }

        /// <summary>
        /// Opens a locked file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public virtual System.IO.Stream OpenLockedRead(string file)
        {
            return System.IO.File.Open(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        }

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The lenth of the file</returns>
        public virtual long GetFileSize(string file)
        {
            return new System.IO.FileInfo(file).Length;
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="file">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public abstract string GetSymlinkTarget(string file);

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="file">The file or folder to examine</param>
        public virtual System.IO.FileAttributes GetAttributes(string file)
        {
            if (file.EndsWith(DIR_SEP))
                return System.IO.File.GetAttributes(file.Substring(0, file.Length - 1));
            else
                return System.IO.File.GetAttributes(file);
        }
        #endregion
    }
}
