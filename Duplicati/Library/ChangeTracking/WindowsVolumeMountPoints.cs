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
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// Lists the paths where volumes are mounted on Windows. A directory change
/// notification on a volume does not report changes under a volume mounted
/// below it, so each mounted volume needs its own watcher.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsVolumeMountPoints
{
    private const int ERROR_NO_MORE_FILES = 18;
    private const int ERROR_MORE_DATA = 234;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindFirstVolumeW")]
    private static extern IntPtr FindFirstVolume(StringBuilder lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindNextVolumeW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextVolume(IntPtr hFindVolume, StringBuilder lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindVolumeClose(IntPtr hFindVolume);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumePathNamesForVolumeNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNamesForVolumeName(string lpszVolumeName, char[] lpszVolumePathNames, uint cchBufferLength, out uint lpcchReturnLength);

    /// <summary>
    /// Gets every path a volume is mounted at, drive roots included, each ending with a separator
    /// </summary>
    /// <returns>The mount paths</returns>
    public static List<string> GetMountPaths()
    {
        var result = new List<string>();
        var volumeName = new StringBuilder(1024);
        var handle = FindFirstVolume(volumeName, (uint)volumeName.Capacity);
        if (handle == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "FindFirstVolume failed");

        try
        {
            do
            {
                result.AddRange(GetPathNames(volumeName.ToString()));
            }
            while (FindNextVolume(handle, volumeName, (uint)volumeName.Capacity));

            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_NO_MORE_FILES)
                throw new Win32Exception(error, "FindNextVolume failed");
        }
        finally
        {
            FindVolumeClose(handle);
        }

        return result;
    }

    private static IEnumerable<string> GetPathNames(string volumeName)
    {
        var buffer = new char[4096];
        if (!GetVolumePathNamesForVolumeName(volumeName, buffer, (uint)buffer.Length, out var length))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_MORE_DATA)
                throw new Win32Exception(error, "GetVolumePathNamesForVolumeName failed");

            buffer = new char[length];
            if (!GetVolumePathNamesForVolumeName(volumeName, buffer, (uint)buffer.Length, out length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetVolumePathNamesForVolumeName failed");
        }

        // The result is a list of strings, each terminated by a null character, ending with an empty string
        var result = new List<string>();
        var start = 0;
        for (var i = 0; i < length; i++)
        {
            if (buffer[i] != '\0')
                continue;

            if (i > start)
                result.Add(new string(buffer, start, i - start));
            start = i + 1;
        }

        return result;
    }
}
