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
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Duplicati.Library.Main.SystemPower;

/// <summary>
/// Shuts down or restarts a Windows computer with the Win32 shutdown API
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsSystemPower
{
    /// <summary>
    /// The name of the privilege that allows shutting down the local computer
    /// </summary>
    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint SE_PRIVILEGE_ENABLED = 0x0002;
    private const int TokenPrivileges = 3;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    /// <summary>
    /// A planned shutdown for application maintenance:
    /// <c>SHTDN_REASON_MAJOR_APPLICATION | SHTDN_REASON_MINOR_MAINTENANCE | SHTDN_REASON_FLAG_PLANNED</c>
    /// </summary>
    private const uint SHUTDOWN_REASON = 0x80040001;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    /// <summary>
    /// <c>TOKEN_PRIVILEGES</c> with room for a single privilege
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privilege;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValueW(string? systemName, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool InitiateSystemShutdownExW(string? machineName, string? message, uint timeout, bool forceAppsClosed, bool rebootAfterShutdown, uint reason);

    /// <summary>
    /// Checks if the account the process runs as holds the shutdown privilege.
    /// The privilege does not need to be enabled, as the process can enable a privilege it holds.
    /// </summary>
    /// <returns>True if the process can shut down the computer</returns>
    public static bool CanShutdown()
    {
        if (!LookupPrivilegeValueW(null, SE_SHUTDOWN_NAME, out var shutdownLuid))
            return false;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
            return false;

        var buffer = IntPtr.Zero;
        try
        {
            // The first call reports the size of the list
            GetTokenInformation(token, TokenPrivileges, IntPtr.Zero, 0, out var length);
            if (length == 0)
                return false;

            buffer = Marshal.AllocHGlobal((int)length);
            if (!GetTokenInformation(token, TokenPrivileges, buffer, length, out _))
                return false;

            // The list is a count followed by the entries
            var count = Marshal.ReadInt32(buffer);
            var entrySize = Marshal.SizeOf<LUID_AND_ATTRIBUTES>();
            for (var i = 0; i < count; i++)
            {
                var entry = Marshal.PtrToStructure<LUID_AND_ATTRIBUTES>(buffer + sizeof(uint) + (i * entrySize));
                if (entry.Luid.LowPart == shutdownLuid.LowPart && entry.Luid.HighPart == shutdownLuid.HighPart)
                    return true;
            }

            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
            CloseHandle(token);
        }
    }

    /// <summary>
    /// Asks Windows to shut down or restart after a delay, during which the logged in users see a warning
    /// </summary>
    /// <param name="reboot">True to restart the computer, false to shut it down</param>
    /// <param name="delaySeconds">The time Windows waits before it shuts down</param>
    /// <param name="message">The message shown to the logged in users</param>
    public static void Perform(bool reboot, uint delaySeconds, string message)
    {
        EnableShutdownPrivilege();

        // The applications are closed without asking, as nobody is expected to be at the computer.
        // This is also what the shutdown command does when it is given a delay.
        if (!InitiateSystemShutdownExW(null, message, delaySeconds, true, reboot, SHUTDOWN_REASON))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>
    /// Enables the shutdown privilege, which is held but disabled in a normal process
    /// </summary>
    private static void EnableShutdownPrivilege()
    {
        if (!LookupPrivilegeValueW(null, SE_SHUTDOWN_NAME, out var shutdownLuid))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var state = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privilege = new LUID_AND_ATTRIBUTES { Luid = shutdownLuid, Attributes = SE_PRIVILEGE_ENABLED }
            };

            // The call also succeeds when the account does not hold the privilege, and reports that as the last error
            var adjusted = AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
            var error = Marshal.GetLastWin32Error();
            if (!adjusted || error == ERROR_NOT_ALL_ASSIGNED)
                throw new Win32Exception(error);
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
