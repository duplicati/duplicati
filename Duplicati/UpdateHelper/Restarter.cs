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
using System.Diagnostics;
using System.IO;
using System.Threading;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper;

/// <summary>
/// Stops supervised instances before the install and starts the application afterwards
/// </summary>
internal static class Restarter
{
    /// <summary>
    /// Asks the supervisor to stop the old instance, where the server cannot exit by itself
    /// </summary>
    public static void StopSupervised(HelperLog log, RestartInstruction restart)
    {
        switch (restart.Kind)
        {
            case UpdateHelperProtocol.RestartSystemd:
                {
                    var systemctl = Shell.Find("systemctl") ?? throw new InvalidOperationException("systemctl was not found");
                    var args = restart.SystemdUserUnit ? new[] { "--user", "stop", restart.SystemdUnit! } : new[] { "stop", restart.SystemdUnit! };
                    var r = Shell.Run(log, systemctl, args, null, TimeSpan.FromMinutes(5));
                    if (!r.Success)
                        throw new InvalidOperationException($"systemctl stop failed with exit code {r.ExitCode}: {r.StdErr.Trim()}");
                    break;
                }
        }
    }

    /// <summary>
    /// Waits for a Windows service to report stopped
    /// </summary>
    public static bool WaitForWindowsServiceStopped(HelperLog log, string serviceName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var state = QueryWindowsServiceState(log, serviceName);
            if (state == null || state.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                return true;
            Thread.Sleep(1000);
        }
        return false;
    }

    /// <summary>
    /// Starts the application after the install
    /// </summary>
    /// <returns>The spawned process when the kind is spawn, otherwise null</returns>
    public static Process? Start(HelperLog log, RestartInstruction restart)
    {
        switch (restart.Kind)
        {
            case UpdateHelperProtocol.RestartNone:
                log.Write("No restart requested");
                return null;

            case UpdateHelperProtocol.RestartSpawn:
                {
                    if (string.IsNullOrWhiteSpace(restart.Executable))
                        throw new InvalidOperationException("No executable to spawn");
                    if (!File.Exists(restart.Executable))
                        throw new FileNotFoundException("Executable to spawn was not found", restart.Executable);

                    var psi = new ProcessStartInfo(restart.Executable)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = string.IsNullOrWhiteSpace(restart.WorkingDirectory) || !Directory.Exists(restart.WorkingDirectory)
                            ? Path.GetDirectoryName(restart.Executable)
                            : restart.WorkingDirectory,
                    };
                    foreach (var a in restart.Arguments)
                        psi.ArgumentList.Add(a);

                    log.Write($"Spawning: {restart.Executable} {string.Join(" ", psi.ArgumentList)}");
                    return Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn the application");
                }

            case UpdateHelperProtocol.RestartSystemd:
                {
                    var systemctl = Shell.Find("systemctl") ?? throw new InvalidOperationException("systemctl was not found");
                    var args = restart.SystemdUserUnit ? new[] { "--user", "start", restart.SystemdUnit! } : new[] { "start", restart.SystemdUnit! };
                    var r = Shell.Run(log, systemctl, args, null, TimeSpan.FromMinutes(5));
                    if (!r.Success)
                        throw new InvalidOperationException($"systemctl start failed with exit code {r.ExitCode}: {r.StdErr.Trim()}");
                    return null;
                }

            case UpdateHelperProtocol.RestartLaunchd:
                {
                    var target = $"gui/{restart.LaunchdUid}/{restart.LaunchdLabel}";
                    var r = Shell.Run(log, "/bin/launchctl", ["kickstart", target], null, TimeSpan.FromMinutes(1));
                    if (!r.Success)
                        throw new InvalidOperationException($"launchctl kickstart failed with exit code {r.ExitCode}: {r.StdErr.Trim()}");
                    return null;
                }

            case UpdateHelperProtocol.RestartWindowsService:
                {
                    var sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
                    var state = QueryWindowsServiceState(log, restart.WindowsServiceName!);
                    if (state != null && state.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                    {
                        log.Write("Service is already running");
                        return null;
                    }

                    var r = Shell.Run(log, sc, ["start", restart.WindowsServiceName!], null, TimeSpan.FromMinutes(1));
                    // 1056: already running, which is fine
                    if (!r.Success && r.ExitCode != 1056)
                        throw new InvalidOperationException($"sc start failed with exit code {r.ExitCode}: {r.StdOut.Trim()}");
                    return null;
                }

            default:
                throw new InvalidOperationException($"Unknown restart kind: {restart.Kind}");
        }
    }

    private static string? QueryWindowsServiceState(HelperLog log, string serviceName)
    {
        try
        {
            var sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
            var r = Shell.Run(log, sc, ["query", serviceName], null, TimeSpan.FromMinutes(1));
            if (!r.Success)
                return null;

            foreach (var line in r.StdOut.Split('\n'))
                if (line.TrimStart().StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                    return line;
            return null;
        }
        catch (Exception ex)
        {
            log.Write($"Failed to query service {serviceName}: {ex.Message}");
            return null;
        }
    }
}
