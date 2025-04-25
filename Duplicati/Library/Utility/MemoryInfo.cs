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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Duplicati.Library.Utility;

/// <summary>
/// Provides methods to retrieve memory information.
/// </summary>
public class MemoryInfo
{
    /// <summary>
    /// Gets the total physical memory in bytes.
    /// </summary>
    /// <returns>Total physical memory in bytes, or zero if this not known</returns>
    public static ulong GetTotalMemory()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetWindowsTotalMemory();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetLinuxTotalMemory();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GetMacTotalMemory();
        }
        catch
        {
        }

        return 0;
    }

    /// <summary>
    /// Returns the total physical memory in a human-readable format.
    /// </summary>
    /// <param name="multiplier">A multiplier added to the memory</param>
    /// <param name="minimum">The minimum value to be returned</param>
    /// <returns>A string representing the total physical memory in a human-readable format.</returns>
    public static string GetTotalMemoryString(double multiplier = 1.0, double minimum = 0)
    {
        var totalMemory = GetTotalMemory();
        if (totalMemory == 0)
            return "0";

        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var index = 0;
        var size = Math.Max(minimum, totalMemory * multiplier);

        while (size >= 1024 && index < suffixes.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{Math.Round(size):0} {suffixes[index]}";
    }

    /// <summary>
    /// Gets the total physical memory in bytes for Windows.
    /// </summary>
    /// <returns>Total physical memory in bytes, or zero if this not known</returns>
    [SupportedOSPlatform("windows")]
    private static ulong GetWindowsTotalMemory()
    {
        var memStatus = new MEMORYSTATUSEX();
        memStatus.dwLength = (uint)Marshal.SizeOf(memStatus);
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            return memStatus.ullTotalPhys;
        }
        return 0;
    }

    /// <summary>
    /// Gets the total physical memory in bytes for Linux.
    /// </summary>
    /// <returns>Total physical memory in bytes, or zero if this not known</returns>
    [SupportedOSPlatform("linux")]
    private static ulong GetLinuxTotalMemory()
    {
        string[] lines = System.IO.File.ReadAllLines("/proc/meminfo");
        foreach (var line in lines)
        {
            if (line.StartsWith("MemTotal:"))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (ulong.TryParse(parts[1], out var kb))
                {
                    return kb * 1024;
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Gets the total physical memory in bytes for macOS.
    /// </summary>
    /// <returns>Total physical memory in bytes, or zero if this not known</returns>
    [SupportedOSPlatform("macos")]
    private static ulong GetMacTotalMemory()
    {
        int mib0 = 6;  // CTL_HW
        int mib1 = 24; // HW_MEMSIZE
        var mib = new[] { mib0, mib1 };
        var size = sizeof(ulong);
        var mem = new byte[size];

        if (sysctl(mib, 2, mem, ref size, IntPtr.Zero, 0) == 0)
            return BitConverter.ToUInt64(mem, 0);

        return 0;
    }

    /// <summary>
    /// GlobalMemoryStatusEx function retrieves information about the system's current usage of both physical and virtual memory.
    /// </summary>
    /// <param name="lpBuffer">The result buffer that will be filled with memory information.</param>
    /// <returns>true if the function succeeds; otherwise, false.</returns>
    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Structure that contains information about the current memory status.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>
    /// sysctl function retrieves system information and configuration parameters.
    /// </summary>
    /// <param name="name">The name of the system information to retrieve.</param>
    /// <param name="namelen">The length of the name array.</param>
    /// <param name="oldp">The buffer that will receive the information.</param>
    /// <param name="oldlenp">The size of the buffer.</param>
    /// <param name="newp">The new value to set (if applicable).</param>
    /// <param name="newlen">The size of the new value.</param>
    /// <returns>0 on success, -1 on error.</returns>
    [SupportedOSPlatform("macos")]
    [DllImport("libc")]
    private static extern int sysctl(int[] name, uint namelen, byte[] oldp, ref int oldlenp, IntPtr newp, uint newlen);
}
