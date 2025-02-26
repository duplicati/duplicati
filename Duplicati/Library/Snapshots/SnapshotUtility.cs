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
using System.IO;
using System.Runtime.Versioning;
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
        /// <param name="paths">The list of paths to create snapshots of</param>
        /// <param name="options">A set of commandline options</param>
        /// <param name="followSymlinks">Whether to follow symlinks</param>
        /// <returns>The ISnapshotService implementation</returns>
        public static ISnapshotService CreateSnapshot(IEnumerable<string> paths, Dictionary<string, string> options, bool followSymlinks)
        {
            if (OperatingSystem.IsLinux())
            {
                return CreateLinuxSnapshot(paths, followSymlinks);
            }
            else if (OperatingSystem.IsWindows())
            {
                return CreateWindowsSnapshot(paths, options, followSymlinks);
            }
            else
            {
                throw new NotSupportedException("Unsupported Operating System");
            }
        }

        /// <summary>
        /// Creates a snapshot implementation that does not use snapshots (i.e., regular file access)
        /// </summary>
        /// <param name="paths">The list of paths to create snapshots of</param>
        /// <param name="ignoreAdvisoryLocking">Flag to ignore advisory locking</param>
        /// <param name="followSymlinks">Whether to follow symlinks</param>
        /// <returns>The ISnapshotService implementation</returns>
        public static ISnapshotService CreateNoSnapshot(IEnumerable<string> paths, bool ignoreAdvisoryLocking, bool followSymlinks)
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                return new NoSnapshotLinux(paths, ignoreAdvisoryLocking, followSymlinks);
            else if (OperatingSystem.IsWindows())
                return new NoSnapshotWindows(paths, followSymlinks);
            else
                throw new NotSupportedException("Unsupported Operating System");

        }

        // The two loader methods below guard against the type system attempting to load types
        // related to the OS specific implementations which may not be present for
        // the operation system we are not running on (i.e. prevent loading AlphaVSS on Linux)

        /// <summary>
        /// Loads a snapshot implementation for Linux
        /// </summary>
        /// <param name="folders">The list of folders to create snapshots of</param>
        /// <param name="followSymlinks">Whether to follow symlinks</param>
        /// <returns>The ISnapshotService implementation</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macOS")]
        private static ISnapshotService CreateLinuxSnapshot(IEnumerable<string> folders, bool followSymlinks)
        {
            return new LinuxSnapshot(folders, followSymlinks);
        }

        /// <summary>
        /// Loads a snapshot implementation for Windows
        /// </summary>
        /// <param name="folders">The list of folders to create snapshots of</param>
        /// <param name="options">A set of commandline options</param>
        /// <param name="followSymlinks">Whether to follow symlinks</param>
        /// <returns>The ISnapshotService implementation</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [SupportedOSPlatform("windows")]
        private static ISnapshotService CreateWindowsSnapshot(IEnumerable<string> folders, Dictionary<string, string> options, bool followSymlinks)
        {
            return new WindowsSnapshot(folders, options, followSymlinks);
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
