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
        protected string[] m_sources;

        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="sourcepaths">The folders that are about to be backed up</param>
        public NoSnapshot(string[] sources)
            : this(sources, new Dictionary<string, string>())
        {
        }

        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="folders">The folders that are about to be backed up</param>
        /// <param name="options">A set of system options</param>
        public NoSnapshot(string[] sources, Dictionary<string, string> options)
        {
            m_sources = new string[sources.Length];
            for (int i = 0; i < m_sources.Length; i++)
                    m_sources[i] = System.IO.Directory.Exists(sources[i]) ? Utility.Utility.AppendDirSeparator(sources[i]) : sources[i];
        }

        #region Private Methods
        /// <summary>
        /// Normalizes a path before calling system methods
        /// </summary>
        /// <returns>The path to normalize.</returns>
        /// <param name="path">The normalized path.</param>
        public static string NormalizePath(string path)
        {
            var p = System.IO.Path.GetFullPath(path);

            // This should not be required, but some versions of Mono apperently do not strip the trailing slash
            return (p.Length > 1 && p[p.Length - 1] == System.IO.Path.DirectorySeparatorChar) ? p.Substring(0, p.Length - 1) : p;
        }

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
        /// <param name="callback">The callback to invoke with each found path</param>
        /// <param name="errorCallback">The callback used to report errors</param>
        public IEnumerable<string> EnumerateFilesAndFolders(Duplicati.Library.Utility.Utility.EnumerationFilterDelegate callback, Duplicati.Library.Utility.Utility.ReportAccessError errorCallback)
        {
            return m_sources.SelectMany(
                s => Utility.Utility.EnumerateFileSystemEntries(s, callback, this.ListFolders, this.ListFiles, this.GetAttributes, errorCallback)
            );
        }

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public virtual DateTime GetLastWriteTimeUtc(string file)
        {
            return System.IO.File.GetLastWriteTimeUtc(file);
        }

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public virtual DateTime GetCreationTimeUtc(string file)
        {
            return System.IO.File.GetCreationTimeUtc(file);
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public virtual System.IO.Stream OpenRead(string file)
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
            return System.IO.File.GetAttributes(NormalizePath(file));
        }
        
        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="file">The file or folder to examine</param>
        /// <param name="isSymlink">A flag indicating if the target is a symlink</param>
        /// <param name="followSymlink">A flag indicating if a symlink should be followed</param>
        public abstract Dictionary<string, string> GetMetadata(string file, bool isSymlink, bool followSymlink);

        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="file">The file or folder to examine</param>
        public abstract bool IsBlockDevice(string file);
        
        /// <summary>
        /// Gets a unique hardlink target ID
        /// </summary>
        /// <returns>The hardlink ID</returns>
        /// <param name="path">The file or folder to examine</param>
        public abstract string HardlinkTargetID(string path);
        #endregion
    }
}
