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
using System.IO;
using System.Threading;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper.Installers;

/// <summary>
/// Installs a macOS pkg with the installer command
/// </summary>
internal sealed class MacPkgInstaller(HelperLog log) : IInstaller
{
    private const string InstallerPath = "/usr/sbin/installer";
    private const string LaunchctlPath = "/bin/launchctl";

    public bool CanRollback => false;
    public bool RestartHandled { get; private set; }
    public IReadOnlyList<string> CleanupPaths => [];

    public string? Prepare(UpdateJob job)
    {
        if (!OperatingSystem.IsMacOS())
            return "pkg packages are only supported on macOS";
        if (!File.Exists(InstallerPath))
            return $"{InstallerPath} was not found";
        return PackageManagerSupport.CheckPrivileges(job);
    }

    public void Install(UpdateJob job)
    {
        var label = job.Restart.LaunchdLabel;
        var domain = $"gui/{job.Restart.LaunchdUid}";

        // The daemon package's postinstall bootstraps the launch agent and fails if it is already loaded
        if (!string.IsNullOrWhiteSpace(label) && File.Exists(LaunchctlPath))
        {
            var bootout = Shell.Run(log, LaunchctlPath, ["bootout", $"{domain}/{label}"], null, TimeSpan.FromMinutes(1));
            if (!bootout.Success)
                log.Write($"launchctl bootout returned {bootout.ExitCode}, continuing");
        }

        var result = ElevatedPackage.Run(log, job, "pkg", $"{Privileges.ShellQuote(InstallerPath)} -pkg {ElevatedPackage.PackageVariable} -target /", "", null, job.InteractiveSession, TimeSpan.FromMinutes(30));
        if (!result.Success)
            throw new InvalidOperationException($"installer failed with exit code {result.ExitCode}: {result.StdErr.Trim()}");

        // The postinstall bootstraps the agent, which starts the app when RunAtLoad is set.
        // Make sure it runs, and skip the generic restart when it does.
        if (!string.IsNullOrWhiteSpace(label) && File.Exists(LaunchctlPath))
        {
            Thread.Sleep(TimeSpan.FromSeconds(2));
            var kick = Shell.Run(log, LaunchctlPath, ["kickstart", $"{domain}/{label}"], null, TimeSpan.FromMinutes(1));
            RestartHandled = kick.Success;
            if (!kick.Success)
                log.Write($"launchctl kickstart returned {kick.ExitCode}, falling back to the restart instruction");
        }
    }

    public void Rollback(UpdateJob job)
    {
    }
}
