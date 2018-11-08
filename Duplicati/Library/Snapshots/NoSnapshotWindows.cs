//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
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
        /// <returns>The lenth of the file</returns>
        public override long GetFileSize (string localPath)
        {
            return SystemIO.IO_WIN.FileLength(localPath);
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
                res[i] = SystemIOWindows.StripUNCPrefix(tmp[i]);

            return res;
        }

        
        /// <summary>
        /// Lists all folders in the given folder
        /// </summary>
        /// <returns>All folders found in the folder</returns>
        /// <param name='localFolderPath'>The folder to examinate</param>
        protected override string[] ListFolders (string localFolderPath)
        {
            string[] tmp = SystemIO.IO_WIN.GetDirectories(SystemIOWindows.PrefixWithUNC(localFolderPath));
            string[] res = new string[tmp.Length];
            for (int i = 0; i < tmp.Length; i++)
                res[i] = SystemIOWindows.StripUNCPrefix(tmp[i]);

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
            return localPath;
        }
    }
}

