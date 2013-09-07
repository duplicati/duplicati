//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
            if (file.EndsWith(DIR_SEP))
                return UnixSupport.File.GetSymlinkTarget(file.Substring(0, file.Length - 1));
            else
                return UnixSupport.File.GetSymlinkTarget(file);
        }
        
        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="file">The file or folder to examine</param>
        public override Dictionary<string, string> GetMetadata(string file)
        {
            var f = file.EndsWith(DIR_SEP) ? file.Substring(0, file.Length - 1) : file;
            var n = UnixSupport.File.GetExtendedAttributes(f);
            var dict = new Dictionary<string, string>();
            foreach(var x in n)
                dict[x.Key] = Convert.ToBase64String(x.Value);
            
            var fse = UnixSupport.File.GetUserGroupAndPermissions(f);
            dict["unix:uid-gid-perm"] = string.Format("{0}-{1}-{2}", fse.UID, fse.GID, fse.Permissions);
            
            return dict;
        }
        
        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="file">The file or folder to examine</param>
        public override bool IsBlockDevice(string file)
        {
            var n = UnixSupport.File.GetFileType(file.EndsWith(DIR_SEP) ? file.Substring(0, file.Length - 1) : file);
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
    }
}

