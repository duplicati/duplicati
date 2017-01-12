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
using System.Text;
using System.Collections.Generic;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Handler for providing a snapshot like access to files and folders
    /// </summary>
    public class NoSnapshotLinux : NoSnapshot
    {
        private readonly SystemIOLinux _sysIO = new SystemIOLinux();

        public NoSnapshotLinux(string[] sourcefolders)
            : base(sourcefolders)
        {
        }

        public NoSnapshotLinux(string[] sourcefolders, Dictionary<string, string> options)
            : base(sourcefolders, options)
        {
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="file">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public override string GetSymlinkTarget(string file)
        {
            return UnixSupport.File.GetSymlinkTarget(NormalizePath(file));
        }
        
        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="file">The file or folder to examine</param>
        /// <param name="isSymlink">A flag indicating if the target is a symlink</param>
        /// <param name="followSymlink">A flag indicating if a symlink should be followed</param>
        public override Dictionary<string, string> GetMetadata(string file, bool isSymlink, bool followSymlink)
        {
            return _sysIO.GetMetadata(file, isSymlink, followSymlink);
        }
        
        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="file">The file or folder to examine</param>
        public override bool IsBlockDevice(string file)
        {
            var n = UnixSupport.File.GetFileType(NormalizePath(file));
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
        /// <param name="file">The file or folder to examine</param>
        public override string HardlinkTargetID(string path)
        {
            path = NormalizePath(path);
            if (UnixSupport.File.GetHardlinkCount(path) <= 1)
                return null;
            
            return UnixSupport.File.GetInodeTargetID(path);
        }
    }
}

