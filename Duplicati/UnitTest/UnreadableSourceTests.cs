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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A source folder that exists but cannot be listed, as on a failed drive, was backed up as an
/// empty folder with only a warning. The new version then had none of its files, and the
/// retention policy removed the versions that had them (#7543).
/// </summary>
public class UnreadableSourceTests : BasicSetupHelper
{
    private string SourceFolder => Path.Combine(DATAFOLDER, "source");

    /// <summary>
    /// Backs up a folder with files, then replaces it with one that exists but cannot be listed
    /// </summary>
    /// <remarks>
    /// On Windows the folder becomes a junction to a folder that is gone, which is how a drive that
    /// is no longer there looks: the junction exists, and listing it fails with an IOException.
    /// Elsewhere the folder loses its permissions, so listing it fails with UnauthorizedAccessException.
    /// </remarks>
    private async Task BackUpAndBreakTheSourceAsync()
    {
        if (!OperatingSystem.IsWindows() && Mono.Unix.Native.Syscall.geteuid() == 0)
            Assert.Ignore("Root can list a folder without permissions");

        Directory.CreateDirectory(SourceFolder);
        for (var i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(SourceFolder, $"file{i}.txt"), $"content {i}");

        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
        {
            var result = await c.BackupAsync(new[] { SourceFolder });
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.ExaminedFiles, Is.EqualTo(5));
        }

        if (OperatingSystem.IsWindows())
        {
            var gone = Path.Combine(DATAFOLDER, "gone");
            Directory.CreateDirectory(gone);
            Directory.Delete(SourceFolder, true);
            using (var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{SourceFolder}\" \"{gone}\"") { UseShellExecute = false, CreateNoWindow = true })!)
                p.WaitForExit();
            Directory.Delete(gone);

            Assert.That(Directory.Exists(SourceFolder), Is.True);
            Assert.Throws<DirectoryNotFoundException>(() => Directory.EnumerateFileSystemEntries(SourceFolder).Any());
        }
        else
        {
            File.SetUnixFileMode(SourceFolder, UnixFileMode.None);
            Assert.Throws<UnauthorizedAccessException>(() => Directory.EnumerateFileSystemEntries(SourceFolder).Any());
        }
    }

    [TearDown]
    public void RestoreTheSource()
    {
        if (!OperatingSystem.IsWindows() && Directory.Exists(SourceFolder))
            File.SetUnixFileMode(SourceFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else if (OperatingSystem.IsWindows() && new DirectoryInfo(SourceFolder).LinkTarget != null)
            Directory.Delete(SourceFolder);
    }

    [Test]
    [Category("Targeted")]
    public async Task ASourceFolderThatCannotBeListedStopsTheBackup()
    {
        await BackUpAndBreakTheSourceAsync();

        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
        {
            var ex = Assert.ThrowsAsync<UserInformationException>(() => c.BackupAsync(new[] { SourceFolder }));
            Assert.That(ex!.HelpID, Is.EqualTo(OperatingSystem.IsWindows() ? "SourceUnreadable" : "SourceUnauthorized"));
        }

        Assert.That(Directory.GetFiles(TARGETFOLDER, "*.dlist.*"), Has.Length.EqualTo(1), "A version was created without the files of the source");
    }

    [Test]
    [Category("Targeted")]
    public async Task AllowMissingSourceStillBacksUpWithoutIt()
    {
        await BackUpAndBreakTheSourceAsync();

        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions.Expand(new { allow_missing_source = true }), null))
        {
            var result = await c.BackupAsync(new[] { SourceFolder });
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.ExaminedFiles, Is.Zero);
        }
    }
}
