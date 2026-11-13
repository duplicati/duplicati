// Copyright (C) 2026, The Duplicati Team
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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// Finds the folders below the source folders where another file system is mounted,
    /// so that --one-file-system can leave them out.
    /// </summary>
    internal static class OtherFileSystems
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(OtherFileSystems));

        /// <summary>
        /// Returns the mount points that lie below one of the sources. A mount point that is a
        /// source itself, or that holds a source, is not returned.
        /// </summary>
        /// <param name="sources">The source paths</param>
        /// <param name="mountPoints">The mount points of the system</param>
        /// <returns>The mount points to leave out, each ending with a directory separator</returns>
        internal static IEnumerable<string> GetMountPointsBelowSources(IEnumerable<string> sources, IEnumerable<string> mountPoints)
        {
            var roots = sources
                // Remote sources are written as "@mountpoint|url" and are not on a local file system
                .Where(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWith("@", StringComparison.Ordinal))
                .Select(s => Util.AppendDirSeparator(s))
                .ToList();

            return mountPoints
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => Util.AppendDirSeparator(m))
                .Distinct(Library.Utility.Utility.ClientFilenameStringComparer)
                .Where(m => roots.Any(r =>
                    m.Length > r.Length && m.StartsWith(r, Library.Utility.Utility.ClientFilenameStringComparison)))
                .ToList();
        }

        /// <summary>
        /// Lists the mount points of the system. On Windows this includes volumes mounted in a folder.
        /// </summary>
        /// <returns>The mount points</returns>
        internal static IEnumerable<string> ListMountPoints()
        {
            if (OperatingSystem.IsWindows())
                return ListWindowsVolumePaths();

            try
            {
                // On Linux and macOS every mount point is listed, including bind mounts and
                // virtual file systems such as /proc
                return DriveInfo.GetDrives().Select(d => d.Name).ToList();
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "MountPointListFailed", ex, "Failed to list the mount points, other file systems are not left out: {0}", ex.Message);
                return [];
            }
        }

        /// <summary>
        /// Lists every path where a volume is mounted, drive letters and folders alike.
        /// </summary>
        /// <returns>The volume paths</returns>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static List<string> ListWindowsVolumePaths()
        {
            var result = new List<string>();
            var volumeName = new StringBuilder(1024);
            var handle = FindFirstVolumeW(volumeName, (uint)volumeName.Capacity);
            if (handle == INVALID_HANDLE_VALUE)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "MountPointListFailed", null, "Failed to list the mount points, other file systems are not left out: error {0}", Marshal.GetLastWin32Error());
                return result;
            }

            try
            {
                do
                {
                    result.AddRange(GetVolumePathNames(volumeName.ToString()));
                } while (FindNextVolumeW(handle, volumeName, (uint)volumeName.Capacity));
            }
            finally
            {
                FindVolumeClose(handle);
            }

            return result;
        }

        /// <summary>
        /// Gets the paths where a volume is mounted.
        /// </summary>
        /// <param name="volumeName">The volume name, as \\?\Volume{GUID}\</param>
        /// <returns>The paths</returns>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static IEnumerable<string> GetVolumePathNames(string volumeName)
        {
            var buffer = new char[1024];
            if (!GetVolumePathNamesForVolumeNameW(volumeName, buffer, (uint)buffer.Length, out var length))
            {
                const int ERROR_MORE_DATA = 234;
                if (Marshal.GetLastWin32Error() != ERROR_MORE_DATA)
                    return [];

                buffer = new char[length];
                if (!GetVolumePathNamesForVolumeNameW(volumeName, buffer, (uint)buffer.Length, out length))
                    return [];
            }

            // The names are separated by a null character and end with two of them
            return new string(buffer, 0, (int)Math.Min(length, (uint)buffer.Length))
                .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindFirstVolumeW(StringBuilder lpszVolumeName, uint cchBufferLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextVolumeW(IntPtr hFindVolume, StringBuilder lpszVolumeName, uint cchBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindVolumeClose(IntPtr hFindVolume);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumePathNamesForVolumeNameW(string lpszVolumeName, [Out] char[] lpszVolumePathNames, uint cchBufferLength, out uint lpcchReturnLength);
    }
}
