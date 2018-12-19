#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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

using System.Collections.Generic;
using System.IO;
using Duplicati.Library.Common;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Simple helper to initialize and load a snapshot implementation for the current OS
    /// </summary>
    public static class SnapshotUtility
    {
        /// <summary>
        /// Loads a snapshot implementation for the current OS
        /// </summary>
        /// <param name="folders">The list of folders to create snapshots of</param>
        /// <param name="options">A set of commandline options</param>
        /// <returns>The ISnapshotService implementation</returns>
        public static ISnapshotService CreateSnapshot(IEnumerable<string> folders, Dictionary<string, string> options)
        {
            return
                Platform.IsClientPosix
                       ? CreateLinuxSnapshot(folders)
                       : CreateWindowsSnapshot(folders, options);
            
        }

        // The two loader methods below guard agains the type system attempting to load types
        // related to the OS specific implementations which may not be present for
        // the operation system we are not running on (i.e. prevent loading AlphaVSS on Linux)

        /// <summary>
        /// Loads a snapshot implementation for Linux
        /// </summary>
        /// <param name="folders">The list of folders to create snapshots of</param>
        /// <returns>The ISnapshotService implementation</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static ISnapshotService CreateLinuxSnapshot(IEnumerable<string> folders)
        {
            return new LinuxSnapshot(folders);
        }

        /// <summary>
        /// Loads a snapshot implementation for Windows
        /// </summary>
        /// <param name="folders">The list of folders to create snapshots of</param>
        /// <param name="options">A set of commandline options</param>
        /// <returns>The ISnapshotService implementation</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static ISnapshotService CreateWindowsSnapshot(IEnumerable<string> folders, Dictionary<string, string> options)
        {
            return new WindowsSnapshot(folders, options);
        }

        /// <summary>
        /// Extension method for ISnapshotService which determines whether the given path is a symlink.
        /// </summary>
        /// <param name="snapshot">ISnapshotService implementation</param>
        /// <param name="path">File or folder path</param>
        /// <returns>Whether the path is a symlink</returns>
        public static bool IsSymlink(this ISnapshotService snapshot, string path)
        {
            return snapshot.IsSymlink(path, snapshot.GetAttributes(path));
        }

        /// <summary>
        /// Extension method for ISnapshotService which determines whether the given path is a symlink.
        /// </summary>
        /// <param name="snapshot">ISnapshotService implementation</param>
        /// <param name="path">File or folder path</param>
        /// <param name="attributes">File attributes</param>
        /// <returns>Whether the path is a symlink</returns>
        public static bool IsSymlink(this ISnapshotService snapshot, string path, FileAttributes attributes)
        {
            // Not all reparse points are symlinks.
            // For example, on Windows 10 Fall Creator's Update, the OneDrive folder (and all subfolders)
            // are reparse points, which allows the folder to hook into the OneDrive service and download things on-demand.
            // If we can't find a symlink target for the current path, we won't treat it as a symlink.
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint && !string.IsNullOrEmpty(snapshot.GetSymlinkTarget(path));
        }

        /// <summary>
        /// Extension method for ISystemIO which determines whether the given path is a symlink.
        /// </summary>
        /// <param name="systemIO">ISystemIO implementation</param>
        /// <param name="path">File or folder path</param>
        /// <returns>Whether the path is a symlink</returns>
        public static bool IsSymlink(this ISystemIO systemIO, string path)
        {
            return systemIO.IsSymlink(path, systemIO.GetFileAttributes(path));
        }

        /// <summary>
        /// Extension method for ISystemIO which determines whether the given path is a symlink.
        /// </summary>
        /// <param name="systemIO">ISystemIO implementation</param>
        /// <param name="path">File or folder path</param>
        /// <param name="attributes">File attributes</param>
        /// <returns>Whether the path is a symlink</returns>
        public static bool IsSymlink(this ISystemIO systemIO, string path, FileAttributes attributes)
        {
            // Not all reparse points are symlinks.
            // For example, on Windows 10 Fall Creator's Update, the OneDrive folder (and all subfolders)
            // are reparse points, which allows the folder to hook into the OneDrive service and download things on-demand.
            // If we can't find a symlink target for the current path, we won't treat it as a symlink.
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint && !string.IsNullOrEmpty(systemIO.GetSymlinkTarget(path));
        }
    }
}
