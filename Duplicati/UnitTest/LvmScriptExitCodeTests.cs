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
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The LVM scripts read "$?" after the test that checked it, so they exited with the exit code of
/// the test, which is 0, and a failed command looked like a success. The scripts run here with
/// stand-ins for the LVM tools on the path.
/// </summary>
[TestFixture]
public class LvmScriptExitCodeTests
{
    private string m_folder = null!;

    [SetUp]
    public void SetUp()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("The LVM scripts run on Linux");

        m_folder = Path.Combine(Path.GetTempPath(), "lvm-script-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(m_folder, "bin"));
    }

    [TearDown]
    public void TearDown()
    {
        if (m_folder != null && Directory.Exists(m_folder))
            Directory.Delete(m_folder, true);
    }

    private void WriteCommand(string name, string body)
    {
        var path = Path.Combine(m_folder, "bin", name);
        File.WriteAllText(path, "#!/bin/bash\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private (int ExitCode, string Output) RunScript(string script, params string[] args)
    {
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "lvm-scripts", script));
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["PATH"] = Path.Combine(m_folder, "bin") + ":" + Environment.GetEnvironmentVariable("PATH");

        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, output.Result + error.Result);
    }

    [Test]
    public void AFailedSnapshotCreationReportsTheExitCode()
    {
        WriteCommand("lvs", "case \"$*\" in *vg_free_count*) echo '  100';; *vg_name*) echo '  vg0';; esac");
        WriteCommand("lvcreate", "exit 5");

        var (exitCode, output) = RunScript("create-lvm-snapshot.sh", "duplicati-snap", "vg0/lv0", m_folder + "/");

        Assert.That(output, Does.Contain("Error: lvcreate failed"));
        Assert.That(exitCode, Is.EqualTo(5), output);
    }

    [Test]
    public void ASnapshotThatCannotBeUnmountedIsNotRemoved()
    {
        var mounted = Path.Combine(m_folder, "mounted");
        Directory.CreateDirectory(mounted);
        var removed = Path.Combine(m_folder, "lvremove-was-called");

        WriteCommand("df", $"printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\n/dev/mapper/vg0-snap 100 1 99 1%% {mounted}\n'");
        WriteCommand("lvs", "case \"$*\" in *vg_name,lv_name*) echo '  vg0 snap';; *origin*) echo '  lv0';; esac");
        WriteCommand("umount", "exit 32");
        WriteCommand("sleep", "exit 0");
        WriteCommand("lvremove", $"touch '{removed}'");

        var (exitCode, output) = RunScript("remove-lvm-snapshot.sh", "duplicati-snap", "vg0/lv0", mounted);

        Assert.That(output, Does.Contain("failed twice"));
        Assert.That(exitCode, Is.EqualTo(32), output);
        Assert.That(File.Exists(removed), Is.False, "A snapshot that is still mounted must not be removed");
    }
}
