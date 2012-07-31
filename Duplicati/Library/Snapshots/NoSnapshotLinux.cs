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
    }
}

