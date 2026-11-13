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

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Duplicati.Library.Common.IO;
using Microsoft.Win32.SafeHandles;

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// Identifies the folder a watcher watches. A watcher follows the folder it was started on,
/// so if another folder takes its path, as when a tool swaps a folder by renaming, the
/// watcher reports nothing for the new one; comparing identities detects that.
/// </summary>
internal static class RootIdentity
{
    /// <summary>
    /// Gets the folder a root is watched through: the root itself, or the folder of a single-file root
    /// </summary>
    /// <param name="root">The root</param>
    /// <returns>The watched folder</returns>
    public static string GetWatchedFolder(string root)
        => File.Exists(root) && !Directory.Exists(root) ? Path.GetDirectoryName(root) ?? root : root;

    /// <summary>
    /// Gets an identity for a folder that stays the same while the folder exists
    /// </summary>
    /// <param name="folder">The folder</param>
    /// <returns>The identity, or null if it cannot be read, as when the folder does not exist</returns>
    public static string? Get(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return null;
            if (OperatingSystem.IsWindows())
                return Windows.Get(folder);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                return PosixFile.GetInodeTargetID(folder);
            return null;
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static class Windows
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_SHARE_ALL = 0x00000001 | 0x00000002 | 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        public static string? Get(string folder)
        {
            using var handle = CreateFile(folder, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
                return null;
            return $"{info.VolumeSerialNumber:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
        }
    }
}
