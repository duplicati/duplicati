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

using System.Collections.Generic;
using Duplicati.Library.AutoUpdater;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The tray icon keeps the files of the installation open. When the server runs in a Windows
/// service, the tray icon runs apart from it, and the update helper must close it before
/// installing, for a zip package as for an MSI package (#8025)
/// </summary>
[TestFixture]
[Category("UpdateInstall")]
public class UpdateTrayIconTerminationTests
{
    private static HostEnvironment MakeHost(SupervisorKind supervisor, string origin)
        => new(
            Origin: origin,
            Supervisor: supervisor,
            ParentPid: -1,
            ProcessPath: @"C:\Duplicati\Duplicati.WindowsService.exe",
            Arguments: [],
            SystemdUnit: null,
            SystemdUserUnit: false,
            SystemdRunPath: null,
            LaunchdLabel: null,
            LaunchdUid: -1,
            WindowsServiceName: supervisor == SupervisorKind.WindowsService ? "Duplicati" : null,
            AppImagePath: null,
            InteractiveSession: supervisor != SupervisorKind.WindowsService);

    /// <summary>
    /// Creates the job the server sends to the helper
    /// </summary>
    private static UpdateJob CreateJob(SupervisorKind supervisor, string origin, string packageTypeId)
        => UpdateHelperLauncher.CreateJob(new UpdateLaunchRequest(
            PackagePath: @"C:\Duplicati\data\updates\package.zip",
            PackageLength: 1,
            PackageSha256: "",
            PackageTypeId: packageTypeId,
            SignedManifest: "",
            TargetVersion: "2.4.1.104",
            CurrentVersion: "2.4.1.103",
            Host: MakeHost(supervisor, origin),
            InstallDir: @"C:\Duplicati",
            HelperSourcePath: @"C:\Duplicati\Duplicati.UpdateHelper.exe",
            WaitPids: [],
            TerminatePids: [],
            UpdatesFolder: @"C:\Duplicati\data\updates",
            MsiProperties: new Dictionary<string, string>(),
            PortableMode: true,
            FinalCheck: () => null), @"C:\Duplicati\data\updates\helper");

    [TestCase("win-x64-gui.zip")]
    [TestCase("win-x64-gui.msi")]
    public void AServiceInstallClosesTheTrayIcon(string packageTypeId)
    {
        var job = CreateJob(SupervisorKind.WindowsService, "Server", packageTypeId);
        Assert.That(job.TerminateProcessNames, Is.EqualTo(new[] { "Duplicati.GUI.TrayIcon" }));
    }

    [TestCase(SupervisorKind.None, "Server", "win-x64-gui.zip")]
    [TestCase(SupervisorKind.None, HostEnvironment.TrayIconOrigin, "win-x64-gui.zip")]
    [TestCase(SupervisorKind.None, "Server", "win-x64-gui.msi")]
    [TestCase(SupervisorKind.WindowsService, HostEnvironment.AgentOrigin, "win-x64-agent.zip")]
    public void OtherInstallsLeaveTheTrayIconAlone(SupervisorKind supervisor, string origin, string packageTypeId)
    {
        var job = CreateJob(supervisor, origin, packageTypeId);
        Assert.That(job.TerminateProcessNames, Is.Empty);
    }
}
