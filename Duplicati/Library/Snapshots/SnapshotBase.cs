// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Duplicati.Library.Common.IO;

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
        public virtual IEnumerable<string> EnumerateFilesAndFolders(IEnumerable<string> sources, Utility.Utility.EnumerationFilterDelegate callback, Utility.Utility.ReportAccessError errorCallback)
        {
            return sources.SelectMany(s => Utility.Utility.EnumerateFileSystemEntries(s, callback, ListFolders, ListFiles, GetAttributes, errorCallback));
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
        /// <returns>The length of the file</returns>
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
            return File.GetAttributes(ConvertToSnapshotPath(localPath));
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
        public virtual bool FileExists(string localFilePath)
        {
            return File.Exists(ConvertToSnapshotPath(localFilePath));
        }

        /// <inheritdoc />
        public virtual bool DirectoryExists(string localFolderPath)
        {
            return Directory.Exists(ConvertToSnapshotPath(localFolderPath));
        }

        /// <inheritdoc />
        public virtual bool PathExists(string localFileOrFolderPath)
        {
            return FileExists(localFileOrFolderPath) || DirectoryExists(localFileOrFolderPath);
        }

        /// <inheritdoc />
        public virtual bool IsSnapshot => false;

        #endregion

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

        #region IDisposable interface

        /// <inheritdoc />
        public void Dispose()
        {
            // We start by calling Dispose(bool) with true
            Dispose(true);

            // Now suppress finalization for this object, since we've already handled our resource cleanup tasks
            GC.SuppressFinalize(this);
        }        

        #endregion

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