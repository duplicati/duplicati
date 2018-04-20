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
using System.IO;

namespace Duplicati.Library.Snapshots
{
    public abstract class SnapshotBase : ISnapshotService
    {
        /// <summary>
        /// Releases unmanaged resources and performs other cleanup operations before the
        /// <see cref="SnapshotBase"/> is reclaimed by garbage collection.
        /// </summary>
        /// <remarks>We must implement a finalizer to guarantee that our native handle is cleaned up</remarks>
        ~SnapshotBase()
        {
            // Our finalizer should call our Dispose(bool) method with false
            Dispose(false);
        }

        #region ISnapshotService
    
        /// <summary>
        /// Enumerates all files and folders in the shadow copy
        /// </summary>
        /// <param name="sources">Source paths to enumerate</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        /// <param name="errorCallback">The callback used to report errors</param>
        public IEnumerable<string> EnumerateFilesAndFolders(IEnumerable<string> sources, Utility.Utility.EnumerationFilterDelegate callback, Utility.Utility.ReportAccessError errorCallback)
        {
            // Add trailing slashes to folders
            var sanitizedSources = sources.Select(x => Directory.Exists(x) ? Utility.Utility.AppendDirSeparator(x) : x).ToList();

            return sanitizedSources.SelectMany(
                s => Utility.Utility.EnumerateFileSystemEntries(s, callback, ListFolders, ListFiles, GetAttributes, errorCallback)
            );
        }
        
        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public virtual DateTime GetLastWriteTimeUtc(string localPath)
        {
            return File.GetLastWriteTimeUtc(localPath);
        }

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public virtual DateTime GetCreationTimeUtc(string localPath)
        {
            return File.GetCreationTimeUtc(localPath);
        }
       
        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public virtual Stream OpenRead(string localPath)
        {
            return File.Open(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The lenth of the file</returns>
        public virtual long GetFileSize(string localPath)
        {
            return new FileInfo(localPath).Length;
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public abstract string GetSymlinkTarget(string localPath);

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public virtual FileAttributes GetAttributes(string localPath)
        {
            return File.GetAttributes(NormalizePath(localPath));
        }

        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="localPath">The file or folder to examine</param>
        /// <param name="isSymlink">A flag indicating if the target is a symlink</param>
        /// <param name="followSymlink">A flag indicating if a symlink should be followed</param>
        public abstract Dictionary<string, string> GetMetadata(string localPath, bool isSymlink, bool followSymlink);
        
        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public virtual bool IsBlockDevice(string localPath)
        {
            return false;
        }
        
        /// <summary>
        /// Gets a unique hardlink target ID
        /// </summary>
        /// <returns>The hardlink ID</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public virtual string HardlinkTargetID(string localPath)
        {
            return null;
        }

        /// <inheritdoc />
        public abstract string ConvertToLocalPath(string snapshotPath);

        /// <inheritdoc />
        public abstract string ConvertToSnapshotPath(string localPath);

        /// <inheritdoc />
        public bool FileExists(string localFilePath)
        {
            return new FileInfo(ConvertToSnapshotPath(localFilePath)).Exists;
        }

        /// <inheritdoc />
        public bool FolderExists(string localFolderPath)
        {
            return new DirectoryInfo(ConvertToSnapshotPath(localFolderPath)).Exists;
        }

        /// <inheritdoc />
        public bool PathExists(string localFileOrFolderPath)
        {
            return FileExists(localFileOrFolderPath) || FolderExists(localFileOrFolderPath);
        }

        #endregion

        /// <summary>
        /// Normalizes a path before calling system methods
        /// </summary>
        /// <returns>The path to normalize.</returns>
        /// <param name="path">The normalized path.</param>
        public static string NormalizePath(string path)
        {
            var p = Path.GetFullPath(path);

            // This should not be required, but some versions of Mono apperently do not strip the trailing slash
            return p.Length > 1 && p[p.Length - 1] == Path.DirectorySeparatorChar ? p.Substring(0, p.Length - 1) : p;
        }

        /// <summary>
        /// Lists all folders in the given folder
        /// </summary>
        /// <returns>All folders found in the folder</returns>
        /// <param name='localFolderPath'>The folder to examinate</param>
        protected virtual string[] ListFolders(string localFolderPath)
        {
            return Directory.GetDirectories(localFolderPath);
        }

        /// <summary>
        /// Lists all files in the given folder
        /// </summary>
        /// <returns>All folders found in the folder</returns>
        /// <param name='localFolderPath'>The folder to examinate</param>
        protected virtual string[] ListFiles(string localFolderPath)
        {
            return Directory.GetFiles(localFolderPath);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // We start by calling Dispose(bool) with true
            Dispose(true);

            // Now suppress finalization for this object, since we've already handled our resource cleanup tasks
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            // nothing to dispose of at this level
        }
    }
}