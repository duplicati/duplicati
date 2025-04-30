// Copyright (C) 2025, The Duplicati Team
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
using System.Runtime.Versioning;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Handler for providing a snapshot like access to files and folders
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class NoSnapshotWindows : SnapshotBase
    {
        /// <summary>
        /// The list of folders to create snapshots of
        /// </summary>
        private readonly IEnumerable<string> m_sources;

        /// <summary>
        /// Initializes a new instance of the <see cref="NoSnapshotWindows"/> class.
        /// </summary>
        /// <param name="sources">The list of entries to create snapshots of</param>
        /// <param name="followSymlinks">A flag indicating if symlinks should be followed</param>
        public NoSnapshotWindows(IEnumerable<string> sources, bool followSymlinks)
            : base(followSymlinks)
        {
            m_sources = sources;
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public override string GetSymlinkTarget(string localPath)
            => SystemIO.IO_OS.GetSymlinkTarget(localPath);

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override System.IO.FileAttributes GetAttributes(string localPath)
            => SystemIO.IO_OS.GetFileAttributes(localPath);

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The length of the file</returns>
        public override long GetFileSize(string localPath)
            => SystemIO.IO_OS.FileLength(localPath);

        /// <summary>
        /// Gets the source folders
        /// </summary>
        public override IEnumerable<string> SourceEntries
            => m_sources;

        /// <summary>
        /// Enumerates the root source files and folders
        /// </summary>
        /// <returns>The source files and folders</returns>
        public override IEnumerable<ISourceProviderEntry> EnumerateFilesystemEntries()
        {
            foreach (var folder in m_sources.Select(SystemIOWindows.RemoveExtendedDevicePathPrefix))
            {
                if (DirectoryExists(folder) || folder.EndsWith(System.IO.Path.DirectorySeparatorChar))
                    yield return new SnapshotSourceFileEntry(this, Util.AppendDirSeparator(folder), true, true);
                else
                    yield return new SnapshotSourceFileEntry(this, folder, false, true);
            }
        }


        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public override DateTime GetLastWriteTimeUtc(string localPath)
            => SystemIO.IO_OS.FileGetLastWriteTimeUtc(localPath);

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public override DateTime GetCreationTimeUtc(string localPath)
            => SystemIO.IO_OS.FileGetCreationTimeUtc(localPath);

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public override System.IO.Stream OpenRead(string localPath)
            => SystemIO.IO_OS.FileOpenRead(localPath);

        /// <summary>
        /// Lists all files in the given folder
        /// </summary>
        /// <returns>All folders found in the folder</returns>
        /// <param name='localFolderPath'>The folder to examinate</param>
        protected override string[] ListFiles(string localFolderPath)
        {
            string[] tmp = SystemIO.IO_OS.GetFiles(localFolderPath);
            string[] res = new string[tmp.Length];
            for (int i = 0; i < tmp.Length; i++)
                res[i] = SystemIOWindows.RemoveExtendedDevicePathPrefix(tmp[i]);

            return res;
        }


        /// <summary>
        /// Lists all folders in the given folder
        /// </summary>
        /// <returns>All folders found in the folder</returns>
        /// <param name='localFolderPath'>The folder to examinate</param>
        protected override string[] ListFolders(string localFolderPath)
        {
            string[] tmp = SystemIO.IO_OS.GetDirectories(SystemIOWindows.AddExtendedDevicePathPrefix(localFolderPath));
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
        public override Dictionary<string, string> GetMetadata(string localPath, bool isSymlink)
            => SystemIO.IO_OS.GetMetadata(localPath, isSymlink, FollowSymlinks);

        /// <inheritdoc />
        public override string ConvertToLocalPath(string snapshotPath)
        {
            return snapshotPath;
        }

        /// <inheritdoc />
        public override string ConvertToSnapshotPath(string localPath) =>
            // For Windows, ensure we don't store paths with extended device path prefixes (i.e., @"\\?\" or @"\\?\UNC\")
            SystemIOWindows.RemoveExtendedDevicePathPrefix(localPath);
    }
}

