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
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper.Installers;

/// <summary>
/// Installs a Debian package with dpkg
/// </summary>
internal sealed class DpkgInstaller(HelperLog log) : IInstaller
{
    public bool CanRollback => false;
    public bool RestartHandled => false;
    public IReadOnlyList<string> CleanupPaths => [];

    public string? Prepare(UpdateJob job)
    {
        if (!OperatingSystem.IsLinux())
            return "Debian packages are only supported on Linux";
        if (Shell.Find("dpkg") == null)
            return "dpkg was not found";
        return PackageManagerSupport.CheckPrivileges(job);
    }

    public void Install(UpdateJob job)
    {
        var dpkg = Shell.Find("dpkg")!;
        var env = new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" };
        var result = ElevatedPackage.Run(log, job, "deb", $"{Privileges.ShellQuote(dpkg)} -i {ElevatedPackage.PackageVariable}", "", env, job.InteractiveSession, TimeSpan.FromMinutes(30));
        if (!result.Success)
            throw new InvalidOperationException($"dpkg failed with exit code {result.ExitCode}: {result.StdErr.Trim()}");
    }

    public void Rollback(UpdateJob job)
    {
    }
}

/// <summary>
/// Shared checks for the package manager based installers
/// </summary>
internal static class PackageManagerSupport
{
    /// <summary>
    /// Checks that root privileges are available, or can be asked for
    /// </summary>
    public static string? CheckPrivileges(UpdateJob job)
    {
        if (ProcessInfo.IsPrivileged())
            return null;
        if (!job.InteractiveSession)
            return "Root privileges are required to install the package and no interactive session is available";
        if (OperatingSystem.IsLinux() && Shell.Find("pkexec") == null)
            return "Root privileges are required to install the package and pkexec was not found";
        return null;
    }
}
