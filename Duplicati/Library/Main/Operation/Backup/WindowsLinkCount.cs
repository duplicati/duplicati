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

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Duplicati.Library.Main.Operation.Backup;

/// <summary>
/// Reads the number of hard links of a file on Windows
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsLinkCount
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
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, System.IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, System.IntPtr hTemplateFile);

    /// <summary>
    /// Gets the number of hard links of a file. Only the attributes are opened, so a file
    /// that another program holds locked can still be checked.
    /// </summary>
    /// <param name="path">The file</param>
    /// <returns>The number of links</returns>
    /// <exception cref="IOException">The link count could not be read</exception>
    public static long Get(string path)
    {
        // A long path is only opened with the extended-length prefix
        if (path.Length >= 248 && !path.StartsWith(@"\\?\", System.StringComparison.Ordinal))
            path = path.StartsWith(@"\\", System.StringComparison.Ordinal) ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;

        using var handle = CreateFile(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, System.IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, System.IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Could not open {path} to read its link count", Marshal.GetHRForLastWin32Error());

        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException($"Could not read the link count of {path}", Marshal.GetHRForLastWin32Error());

        return info.NumberOfLinks;
    }
}
