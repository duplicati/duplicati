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
using System.IO;
using System.Threading;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper.Installers;

/// <summary>
/// Installs an MSI package with msiexec; the installer stops and starts the Windows service itself
/// </summary>
internal sealed class MsiInstaller(HelperLog log) : IInstaller
{
    /// <summary>
    /// Exit code for success
    /// </summary>
    private const int ERROR_SUCCESS = 0;
    /// <summary>
    /// Exit code for success where a reboot was requested, but suppressed
    /// </summary>
    private const int ERROR_SUCCESS_REBOOT_REQUIRED = 3010;
    /// <summary>
    /// Exit code when the user declined the elevation prompt
    /// </summary>
    private const int ERROR_INSTALL_USEREXIT = 1602;
    /// <summary>
    /// The global mutex held while an install runs
    /// </summary>
    private const string MsiExecuteMutex = @"Global\_MSIExecute";

    public bool CanRollback => false;
    public bool RestartHandled => false;
    public IReadOnlyList<string> CleanupPaths => [];

    public string? Prepare(UpdateJob job)
    {
        if (!OperatingSystem.IsWindows())
            return "MSI packages are only supported on Windows";
        if (!File.Exists(MsiExecPath))
            return $"msiexec not found at {MsiExecPath}";
        if (!ProcessInfo.IsPrivileged() && !job.InteractiveSession)
            return "Administrator privileges are required to install the MSI package and no interactive session is available";
        if (IsInstallerBusy())
            return "Another installation is in progress";

        return null;
    }

    public void Install(UpdateJob job)
    {
        var logFile = Path.Combine(Path.GetDirectoryName(job.LogFile) ?? Path.GetTempPath(), "msiexec.log");
        var elevated = ProcessInfo.IsPrivileged();

        // The package sits in a folder the user can write to. Holding it open without write or delete sharing
        // keeps it from being changed or replaced between this check and msiexec reading it.
        using var hold = new FileStream(job.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var problem = PackageVerifier.VerifyContent(hold, job);
        if (problem != null)
            throw new InvalidOperationException($"The package changed after it was verified: {problem}");

        var psi = new ProcessStartInfo(MsiExecPath)
        {
            UseShellExecute = !elevated,
            CreateNoWindow = true,
        };
        if (!elevated)
            psi.Verb = "runas";

        psi.ArgumentList.Add("/i");
        psi.ArgumentList.Add(job.PackagePath);
        psi.ArgumentList.Add("/qn");
        psi.ArgumentList.Add("/norestart");
        psi.ArgumentList.Add("/l*v");
        psi.ArgumentList.Add(logFile);
        psi.ArgumentList.Add("REBOOT=ReallySuppress");
        foreach (var kv in job.MsiProperties)
            psi.ArgumentList.Add($"{kv.Key}={kv.Value}");

        log.Write($"Running: {MsiExecPath} {string.Join(" ", psi.ArgumentList)} (elevated: {elevated})");

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start msiexec");
        if (!p.WaitForExit(TimeSpan.FromMinutes(30)))
        {
            try { p.Kill(); } catch { }
            throw new TimeoutException("msiexec did not complete within 30 minutes");
        }

        log.Write($"msiexec exit code {p.ExitCode}, log in {logFile}");
        if (p.ExitCode == ERROR_INSTALL_USEREXIT)
            throw new InvalidOperationException("The installation was cancelled at the elevation prompt");
        if (p.ExitCode != ERROR_SUCCESS && p.ExitCode != ERROR_SUCCESS_REBOOT_REQUIRED)
            throw new InvalidOperationException($"msiexec failed with exit code {p.ExitCode}, see {logFile}");
    }

    public void Rollback(UpdateJob job)
    {
    }

    private static string MsiExecPath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");

    private static bool IsInstallerBusy()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var m = Mutex.OpenExisting(MsiExecuteMutex);
            // The mutex exists while an install runs; try to take it without waiting
            if (m.WaitOne(0))
            {
                m.ReleaseMutex();
                return false;
            }
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
