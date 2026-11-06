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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// This file is compiled into both Duplicati.Library.AutoUpdater (the server side)
// and Duplicati.UpdateHelper (the stand-alone helper), see UpdateHelperProtocol.cs.
namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// Information about the current process that the framework does not expose
/// </summary>
public static class ProcessInfo
{
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, int inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

    /// <summary>
    /// Returns true if the process runs as root (Unix) or as an elevated administrator (Windows)
    /// </summary>
    public static bool IsPrivileged()
    {
        if (OperatingSystem.IsWindows())
            return IsWindowsElevated();

        try { return GetEuid() == 0; }
        catch { return false; }
    }

    /// <summary>
    /// The real user id on Unix, or -1 on Windows
    /// </summary>
    public static int UnixUserId()
    {
        if (OperatingSystem.IsWindows())
            return -1;
        try { return (int)GetUid(); }
        catch { return -1; }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the parent process id of this process, or -1 if it cannot be determined
    /// </summary>
    public static int GetParentProcessId()
        => GetParentProcessId(Environment.ProcessId);

    /// <summary>
    /// Gets the parent process id of a process, or -1 if it cannot be determined
    /// </summary>
    /// <param name="pid">The process id</param>
    public static int GetParentProcessId(int pid)
    {
        if (pid <= 0)
            return -1;

        try
        {
            if (OperatingSystem.IsWindows())
                return GetWindowsParentProcessId(pid);
            if (OperatingSystem.IsLinux())
                return GetLinuxParentProcessId(pid);
            if (OperatingSystem.IsMacOS())
                return GetParentProcessIdWithPs(pid);
        }
        catch
        {
        }

        return -1;
    }

    [SupportedOSPlatform("windows")]
    private static int GetWindowsParentProcessId(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if (handle == IntPtr.Zero)
            return -1;

        try
        {
            var info = new PROCESS_BASIC_INFORMATION();
            var status = NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
            return status == 0 ? (int)info.InheritedFromUniqueProcessId : -1;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static int GetLinuxParentProcessId(int pid)
    {
        // The second field is the executable name in parentheses and may contain spaces,
        // so the fields are taken from after the closing parenthesis: state, ppid, ...
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var idx = stat.LastIndexOf(')');
        if (idx < 0)
            return -1;
        var fields = stat.Substring(idx + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1 && int.TryParse(fields[1], out var ppid) ? ppid : -1;
    }

    private static int GetParentProcessIdWithPs(int pid)
    {
        var psi = new ProcessStartInfo("/bin/ps")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("ppid=");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(pid.ToString());

        using var p = Process.Start(psi);
        if (p == null)
            return -1;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return int.TryParse(output.Trim(), out var ppid) ? ppid : -1;
    }

    /// <summary>
    /// Gets the executable path of a process, or null if not accessible
    /// </summary>
    public static string? GetProcessPath(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
