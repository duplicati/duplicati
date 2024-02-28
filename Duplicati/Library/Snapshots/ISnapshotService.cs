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
using System.Collections.Generic;
using System.IO;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// An interface for a snapshot implementation
    /// </summary>
    public interface ISnapshotService : IDisposable
    {
        /// <summary>
        /// Enumerates all files and folders in the snapshot, restricted to sources
        /// </summary>
        /// <param name="sources">Sources to enumerate</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        /// <param name="errorCallback">The callback used to report errors</param>
        IEnumerable<string> EnumerateFilesAndFolders(IEnumerable<string> sources, Utility.Utility.EnumerationFilterDelegate callback, Utility.Utility.ReportAccessError errorCallback);

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        DateTime GetLastWriteTimeUtc(string localPath);

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        DateTime GetCreationTimeUtc(string localPath);

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read and seeked</returns>
        Stream OpenRead(string localPath);

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The length of the file</returns>
        long GetFileSize(string localPath);

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        string GetSymlinkTarget(string localPath);

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="localPath">The file or folder to examine</param>
        FileAttributes GetAttributes(string localPath);
        
        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="localPath">The file or folder to examine</param>
        /// <param name="isSymlink">A flag indicating if the target is a symlink</param>
        /// <param name="followSymlink">A flag indicating if a symlink should be followed</param>
        Dictionary<string, string> GetMetadata(string localPath, bool isSymlink, bool followSymlink);
        
        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="localPath">The file or folder to examine</param>
        bool IsBlockDevice(string localPath);
        
        /// <summary>
        /// Gets a unique hardlink target ID
        /// </summary>
        /// <returns>The hardlink ID</returns>
        /// <param name="localPath">The file or folder to examine</param>
        string HardlinkTargetID(string localPath);

        /// <summary>
        /// Converts a snapshot path to a local path
        /// </summary>
        /// <param name="snapshotPath">The snapshot-local path</param>
        /// <returns>The local path</returns>
        string ConvertToLocalPath(string snapshotPath);

        /// <summary>
        /// Converts a local path to a snapshot-local path
        /// </summary>
        /// <param name="localPath">The local path</param>
        /// <returns>The snapshot path</returns>
        string ConvertToSnapshotPath(string localPath);

        /// <summary>
        /// Tests if a file exists in the snapshot
        /// </summary>
        /// <param name="localFilePath">The local path</param>
        /// <returns>True if file exists, false otherwise</returns>
        bool FileExists(string localFilePath);

        /// <summary>
        /// Tests if a folder exists in the snapshot
        /// </summary>
        /// <param name="localFolderPath">The local path</param>
        /// <returns>True if folder exists, false otherwise</returns>
        bool DirectoryExists(string localFolderPath);

        /// <summary>
        /// Tests if a file or folder exists in the snapshot
        /// </summary>
        /// <param name="localFileOrFolderPath">The local path</param>
        /// <returns>True if file or folder exists, false otherwise</returns>
        bool PathExists(string localFileOrFolderPath);

        /// <summary>
        /// Is true if the actual service is a real snapshot service
        /// </summary>
        bool IsSnapshot { get; }
    }
}
