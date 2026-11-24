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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Rclone = Duplicati.Library.Backend.Rclone;
using Duplicati.Server.Database;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// An encrypted rclone configuration needs its password, which rclone reads from RCLONE_CONFIG_PASS (#7974)
/// </summary>
[TestFixture]
[Category("Backend")]
public class RcloneConfigPasswordTests
{
    private string m_folder = "";

    [SetUp]
    public void SetUp()
    {
        m_folder = Path.Combine(Path.GetTempPath(), "duplicati-rclone-" + Path.GetRandomFileName());
        Directory.CreateDirectory(m_folder);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_folder, true); }
        catch { }
    }

    private string EnvironmentFile => Path.Combine(m_folder, "environment.txt");
    private string ArgumentsFile => Path.Combine(m_folder, "arguments.txt");

    /// <summary>
    /// Creates a fake rclone that writes the password it was given and its arguments to files, and lists no files
    /// </summary>
    private string CreateFakeRclone()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(m_folder, "fake-rclone.cmd");
            File.WriteAllText(path,
                $"@echo.%{Rclone.RCLONE_CONFIG_PASS}%> \"{EnvironmentFile}\"\r\n" +
                $"@echo.%*> \"{ArgumentsFile}\"\r\n" +
                "@echo []\r\n");
            return path;
        }
        else
        {
            var path = Path.Combine(m_folder, "fake-rclone.sh");
            File.WriteAllText(path,
                "#!/bin/sh\n" +
                $"printf '%s' \"${Rclone.RCLONE_CONFIG_PASS}\" > '{EnvironmentFile}'\n" +
                $"printf '%s' \"$*\" > '{ArgumentsFile}'\n" +
                "echo '[]'\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
    }

    private async Task ListAsync(Dictionary<string, string?> options)
    {
        options["rclone-executable"] = CreateFakeRclone();
        var backend = new Rclone("rclone://remote/backup", options);
        await foreach (var _ in backend.ListAsync(CancellationToken.None))
        { }
    }

    [Test]
    public async Task ThePasswordIsPassedInTheEnvironmentAsync()
    {
        await ListAsync(new Dictionary<string, string?> { ["rclone-config-password"] = "s3cret-pass" });

        Assert.That(File.ReadAllText(EnvironmentFile).Trim(), Is.EqualTo("s3cret-pass"));
        Assert.That(File.ReadAllText(ArgumentsFile), Does.Contain("lsjson remote:"));
        Assert.That(File.ReadAllText(ArgumentsFile), Does.Not.Contain("s3cret-pass"));
    }

    [Test]
    public async Task NoPasswordIsSetWithoutTheOptionAsync()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Rclone.RCLONE_CONFIG_PASS)))
            Assert.Ignore($"{Rclone.RCLONE_CONFIG_PASS} is set for the test process");

        await ListAsync(new Dictionary<string, string?>());

        Assert.That(File.ReadAllText(EnvironmentFile).Trim(), Is.Empty);
    }

    /// <summary>
    /// A password option is stored encrypted and is masked when the server sends the backup to the UI
    /// </summary>
    [Test]
    public void TheOptionIsMaskedAndEncryptedByTheServer()
        => Assert.That(Connection.PasswordFieldNames, Does.Contain("rclone-config-password").And.Contain("--rclone-config-password"));
}
