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
using System.Linq;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Handler for providing a snapshot like access to files and folders
    /// </summary>
    public sealed class NoSnapshotWindows : SnapshotBase
    {
        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public override string GetSymlinkTarget(string localPath)
        {
            return SystemIO.IO_WIN.GetSymlinkTarget(localPath);
        }

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override System.IO.FileAttributes GetAttributes (string localPath)
        {
            return SystemIO.IO_WIN.GetFileAttributes(localPath);
        }

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The length of the file</returns>
        public override long GetFileSize (string localPath)
        {
            return SystemIO.IO_WIN.FileLength(localPath);
        }

        /// <summary>
        /// Enumerates all files and folders in the snapshot, restricted to sources
        /// </summary>
        /// <param name="sources">Sources to enumerate</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        /// <param name="errorCallback">The callback used to report errors</param>
        public override IEnumerable<string> EnumerateFilesAndFolders(IEnumerable<string> sources, Utility.Utility.EnumerationFilterDelegate callback, Utility.Utility.ReportAccessError errorCallback)
        {
            // For Windows, ensure we don't store paths with extended device path prefixes (i.e., @"\\?\" or @"\\?\UNC\")
            return base.EnumerateFilesAndFolders(sources.Select(SystemIOWindows.RemoveExtendedDevicePathPrefix), callback, errorCallback);
        }

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public override DateTime GetLastWriteTimeUtc (string localPath)
        {
            return SystemIO.IO_WIN.FileGetLastWriteTimeUtc(localPath);
        }

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public override DateTime GetCreationTimeUtc (string localPath)
        {
            return SystemIO.IO_WIN.FileGetCreationTimeUtc(localPath);
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public override System.IO.Stream OpenRead (string localPath)
        {
            return SystemIO.IO_WIN.FileOpenRead(localPath);
        }

        /// <summary>
        /// Lists all files in the given folder
        /// </summary>
        /// <returns>All folders found in the folder</returns>
        /// <param name='localFolderPath'>The folder to examinate</param>
        protected override string[] ListFiles (string localFolderPath)
        {
            string[] tmp = SystemIO.IO_WIN.GetFiles(localFolderPath);
            string[] res = new string[tmp.Length];
            for(int i = 0; i < tmp.Length; i++)
                res[i] = SystemIOWindows.RemoveExtendedDevicePathPrefix(tmp[i]);

            return res;
        }

        
        /// <summary>
        /// Lists all folders in the given folder
        /// </summary>
        /// <returns>All folders found in the folder</returns>
        /// <param name='localFolderPath'>The folder to examinate</param>
        protected override string[] ListFolders (string localFolderPath)
        {
            string[] tmp = SystemIO.IO_WIN.GetDirectories(SystemIOWindows.AddExtendedDevicePathPrefix(localFolderPath));
            string[] res = new string[tmp.Length];
            for (int i = 0; i < tmp.Length; i++)
                res[i] = SystemIOWindows.RemoveExtendedDevicePathPrefix(tmp[i]);

            return res;
        }

        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="localPath">The file or folder to examine</param>
        /// <param name="isSymlink">A flag indicating if the target is a symlink</param>
        /// <param name="followSymlink">A flag indicating if a symlink should be followed</param>
        public override Dictionary<string, string> GetMetadata(string localPath, bool isSymlink, bool followSymlink)
        {
            return SystemIO.IO_WIN.GetMetadata(localPath, isSymlink, followSymlink);
        }

        /// <inheritdoc />
        public override string ConvertToLocalPath(string snapshotPath)
        {
            return snapshotPath;
        }

        /// <inheritdoc />
        public override string ConvertToSnapshotPath(string localPath)
        {
            // For Windows, ensure we don't store paths with extended device path prefixes (i.e., @"\\?\" or @"\\?\UNC\")
            return SystemIOWindows.RemoveExtendedDevicePathPrefix(localPath);
        }
    }
}

