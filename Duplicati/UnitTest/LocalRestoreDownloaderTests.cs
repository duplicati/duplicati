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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A restore read half as many volumes at once as there are processors, which made a restore
/// from an external hard disk slow, so a local folder is read by one downloader unless told otherwise (#6829)
/// </summary>
public class LocalRestoreDownloaderTests : BasicSetupHelper
{
    private static int DownloadersFor(string? backendUrl, Dictionary<string, string?>? options = null)
        => new Options(options ?? []).GetRestoreVolumeDownloaders(backendUrl);

    [Test]
    [Category("Options")]
    [TestCase("file://C:\\backup")]
    [TestCase("file:///mnt/usb/backup")]
    [TestCase("C:\\backup")]
    [TestCase("/mnt/usb/backup")]
    public void ALocalFolderIsReadByOneDownloader(string backendUrl)
        => Assert.That(DownloadersFor(backendUrl), Is.EqualTo(1));

    [Test]
    [Category("Options")]
    [TestCase("s3://bucket/prefix")]
    [TestCase("ftp://example.com/backup")]
    [TestCase(null)]
    public void OtherDestinationsKeepTheDefault(string? backendUrl)
        => Assert.That(DownloadersFor(backendUrl), Is.EqualTo(Options.DEFAULT_RESTORE_VOLUME_DOWNLOADERS));

    [Test]
    [Category("Options")]
    public void AGivenNumberIsUsedForALocalFolder()
        => Assert.That(DownloadersFor("file://C:\\backup", new() { ["restore-volume-downloaders"] = "3" }), Is.EqualTo(3));

    /// <summary>
    /// Restores a backup of several volumes from a local folder and counts the volume
    /// downloaders that were started, from the line each one logs when it stops
    /// </summary>
    private async Task<int> DownloadersStartedByRestore(Dictionary<string, string> extra)
    {
        var rng = new Random(42);
        var data = new byte[64 * 1024];
        for (var i = 0; i < 20; i++)
        {
            rng.NextBytes(data);
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"file{i}"), data);
        }

        var backupOptions = new Dictionary<string, string>(this.TestOptions)
        {
            ["dblock-size"] = "100kb",
            ["blocksize"] = "4kb",
        };
        using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
            TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

        var logFile = Path.Combine(BASEFOLDER, "restore-downloaders.log");
        File.Delete(logFile);
        var restoreOptions = new Dictionary<string, string>(this.TestOptions)
        {
            ["restore-path"] = this.RESTOREFOLDER,
            ["blocksize"] = "4kb",
            ["log-file"] = logFile,
            ["log-file-log-level"] = "Verbose",
        };
        foreach (var (key, value) in extra)
            restoreOptions[key] = value;

        using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

        Assert.That(Directory.EnumerateFiles(this.RESTOREFOLDER, "*", SearchOption.AllDirectories).Count(), Is.EqualTo(20));
        return File.ReadLines(logFile).Count(x => x.Contains("Volume downloader retired"));
    }

    [Test]
    [Category("Targeted")]
    public async Task ARestoreFromALocalFolderUsesOneDownloaderAsync()
        => Assert.That(await DownloadersStartedByRestore([]), Is.EqualTo(1));

    [Test]
    [Category("Targeted")]
    public async Task ARestoreFromALocalFolderUsesAGivenNumberAsync()
        => Assert.That(await DownloadersStartedByRestore(new() { ["restore-volume-downloaders"] = "3" }), Is.EqualTo(3));
}
