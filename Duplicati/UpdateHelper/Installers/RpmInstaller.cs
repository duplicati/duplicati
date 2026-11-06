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
/// Installs an RPM package with rpm
/// </summary>
internal sealed class RpmInstaller(HelperLog log) : IInstaller
{
    public bool CanRollback => false;
    public bool RestartHandled => false;
    public IReadOnlyList<string> CleanupPaths => [];

    public string? Prepare(UpdateJob job)
    {
        if (!OperatingSystem.IsLinux())
            return "RPM packages are only supported on Linux";
        if (Shell.Find("rpm") == null)
            return "rpm was not found";
        return PackageManagerSupport.CheckPrivileges(job);
    }

    public void Install(UpdateJob job)
    {
        var rpm = Shell.Find("rpm")!;
        var result = ElevatedPackage.Run(log, job, "rpm", $"{Privileges.ShellQuote(rpm)} -U {ElevatedPackage.PackageVariable}", "", null, job.InteractiveSession, TimeSpan.FromMinutes(30));
        if (!result.Success)
            throw new InvalidOperationException($"rpm failed with exit code {result.ExitCode}: {result.StdErr.Trim()}");
    }

    public void Rollback(UpdateJob job)
    {
    }
}
