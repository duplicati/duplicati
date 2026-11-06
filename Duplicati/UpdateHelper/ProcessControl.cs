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
using System.Diagnostics;
using System.Threading;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper;

/// <summary>
/// Waits for and terminates processes
/// </summary>
internal static class ProcessControl
{
    /// <summary>
    /// Returns true if a process with the given id is still running
    /// </summary>
    public static bool IsRunning(int pid)
    {
        if (pid <= 0)
            return false;

        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits until none of the processes are running
    /// </summary>
    /// <param name="log">The log</param>
    /// <param name="pids">The process ids</param>
    /// <param name="timeout">The maximum time to wait</param>
    /// <returns>True if all processes exited, false on timeout</returns>
    public static bool WaitForExit(HelperLog log, IEnumerable<int> pids, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var remaining = new List<int>(pids);
        var lastReport = DateTime.MinValue;

        while (remaining.Count > 0)
        {
            remaining.RemoveAll(pid => !IsRunning(pid));
            if (remaining.Count == 0)
                return true;

            if (DateTime.UtcNow > deadline)
            {
                log.Write($"Timeout waiting for processes to exit: {string.Join(", ", remaining)}");
                return false;
            }

            if (DateTime.UtcNow - lastReport > TimeSpan.FromSeconds(10))
            {
                log.Write($"Waiting for processes to exit: {string.Join(", ", remaining)}");
                lastReport = DateTime.UtcNow;
            }

            Thread.Sleep(250);
        }

        return true;
    }

    /// <summary>
    /// Terminates the process with the given id, if it runs
    /// </summary>
    public static void Terminate(HelperLog log, int pid)
    {
        if (pid <= 0)
            return;

        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited)
                return;
            log.Write($"Terminating process {pid} ({SafeName(p)})");
            p.Kill();
            p.WaitForExit(10000);
        }
        catch (ArgumentException)
        {
        }
        catch (Exception ex)
        {
            log.Write($"Failed to terminate process {pid}: {ex.Message}");
        }
    }

    /// <summary>
    /// Terminates all processes with the given name
    /// </summary>
    public static void TerminateByName(HelperLog log, string name)
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName(name); }
        catch (Exception ex)
        {
            log.Write($"Failed to enumerate processes named {name}: {ex.Message}");
            return;
        }

        foreach (var p in processes)
        {
            using (p)
            {
                try
                {
                    if (p.HasExited)
                        continue;
                    log.Write($"Terminating process {p.Id} ({name})");
                    p.Kill();
                    p.WaitForExit(10000);
                }
                catch (Exception ex)
                {
                    log.Write($"Failed to terminate process {p.Id}: {ex.Message}");
                }
            }
        }
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName; }
        catch { return "?"; }
    }

    /// <summary>
    /// Checks whether a process is the given ancestor or one of its descendants
    /// </summary>
    /// <param name="pid">The process to check</param>
    /// <param name="ancestorPid">The possible ancestor</param>
    /// <returns>True if related, false if the process tree was resolved and they are not related, null if the tree could not be resolved</returns>
    public static bool? IsDescendantOf(int pid, int ancestorPid)
    {
        if (pid <= 0 || ancestorPid <= 0)
            return null;

        var current = pid;
        for (var depth = 0; depth < 16; depth++)
        {
            if (current == ancestorPid)
                return true;

            var parent = ProcessInfo.GetParentProcessId(current);
            if (parent < 0)
                return null;

            // The root of the tree: init or launchd on Unix, the idle process on Windows
            if (parent <= 1 || parent == current)
                return false;

            current = parent;
        }

        return null;
    }
}
