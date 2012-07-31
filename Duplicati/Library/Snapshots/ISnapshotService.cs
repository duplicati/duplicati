#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
#endregion
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
        /// Enumerates all files and folders in the snapshot
        /// </summary>
        /// <param name="callback">The callback to invoke with each found path</param>
        void EnumerateFilesAndFolders(Duplicati.Library.Utility.Utility.EnumerationCallbackDelegate callback);

        /// <summary>
        /// Enumerates all files and folders in the snapshot
        /// </summary>
        /// <param name="startpath">The path from which to retrieve files and folders</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        void EnumerateFilesAndFolders(string startpath, Duplicati.Library.Utility.Utility.EnumerationCallbackDelegate callback);

        /// <summary>
        /// Gets the last write time of a given file
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        DateTime GetLastWriteTime(string file);

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read and seeked</returns>
        Stream OpenRead(string file);

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="file">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        string GetSymlinkTarget(string file);

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="file">The file or folder to examine</param>
        System.IO.FileAttributes GetAttributes(string file);
    }
}
