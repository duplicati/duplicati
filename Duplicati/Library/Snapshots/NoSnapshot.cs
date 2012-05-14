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
    public class NoSnapshot : ISnapshotService
    {
        /// <summary>
        /// The list of all folders in the snapshot
        /// </summary>
        private string[] m_sourcefolders;

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
        /// <param name="attemptDirtyAccess">True if the file may be opened with read/write permissions</param>
        public NoSnapshot(string[] folders, Dictionary<string, string> options)
        {
            m_sourcefolders = new string[folders.Length];
            for (int i = 0; i < m_sourcefolders.Length; i++)
                m_sourcefolders[i] = Utility.Utility.AppendDirSeparator(folders[i]);
        }

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
        /// <param name="filter">The filter to apply when evaluating files and folders</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        public void EnumerateFilesAndFolders(string startpath, Duplicati.Library.Utility.FilenameFilter filter, Duplicati.Library.Utility.Utility.EnumerationCallbackDelegate callback)
        {
            foreach (string s in m_sourcefolders)
                if (s.Equals(startpath, Utility.Utility.ClientFilenameStringComparision))
                {
                    Utility.Utility.EnumerateFileSystemEntries(s, filter, callback);
                    return;
                }

            throw new InvalidOperationException(string.Format(Strings.Shared.InvalidEnumPathError, startpath));
        }

        /// <summary>
        /// Enumerates all files and folders in the snapshot
        /// </summary>
        /// <param name="filter">The filter to apply when evaluating files and folders</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        public void EnumerateFilesAndFolders(Duplicati.Library.Utility.FilenameFilter filter, Duplicati.Library.Utility.Utility.EnumerationCallbackDelegate callback)
        {
            foreach (string s in m_sourcefolders)
                Utility.Utility.EnumerateFileSystemEntries(s, filter, callback);
        }

        /// <summary>
        /// Gets the last write time of a given file
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public DateTime GetLastWriteTime(string file)
        {
            return System.IO.File.GetLastWriteTime(file);
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public System.IO.Stream OpenRead(string file)
        {
            return System.IO.File.OpenRead(file);
        }

        /// <summary>
        /// Opens a locked file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public System.IO.Stream OpenLockedRead(string file)
        {
            return System.IO.File.Open(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        }

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The lenth of the file</returns>
        public long GetFileSize(string file)
        {
            return new System.IO.FileInfo(file).Length;
        }
        #endregion
    }
}
