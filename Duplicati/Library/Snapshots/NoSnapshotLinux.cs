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

using System.Collections.Generic;
using Duplicati.Library.IO;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Handler for providing a snapshot like access to files and folders
    /// </summary>
    public sealed class NoSnapshotLinux : SnapshotBase
    {
        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public override string GetSymlinkTarget(string localPath)
        {
            return SystemIO.IO_SYS.GetSymlinkTarget(localPath);
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
            return SystemIO.IO_SYS.GetMetadata(localPath, isSymlink, followSymlink);
        }
        
        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override bool IsBlockDevice(string localPath)
        {
            var n = UnixSupport.File.GetFileType(SystemIOLinux.NormalizePath(localPath));
            switch (n)
            {
                case UnixSupport.File.FileType.Directory:
                case UnixSupport.File.FileType.Symlink:
                case UnixSupport.File.FileType.File:
                    return false;
                default:
                    return true;
            }
        }
        
        /// <summary>
        /// Gets a unique hardlink target ID
        /// </summary>
        /// <returns>The hardlink ID</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override string HardlinkTargetID(string localPath)
        {
            var normalizePath = SystemIOLinux.NormalizePath(localPath);
            return UnixSupport.File.GetHardlinkCount(normalizePath) <= 1
                ? null
                : UnixSupport.File.GetInodeTargetID(normalizePath);
        }

        /// <inheritdoc />
        public override string ConvertToLocalPath(string snapshotPath)
        {
            return snapshotPath;
        }

        /// <inheritdoc />
        public override string ConvertToSnapshotPath(string localPath)
        {
            return SystemIOLinux.NormalizePath(localPath);
        }
    }
}

