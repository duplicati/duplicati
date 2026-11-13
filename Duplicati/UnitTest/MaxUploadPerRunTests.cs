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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A destination with a daily or monthly transfer cap rejected the uploads once a run went over it,
/// and nothing could keep a run under it, so --max-upload-per-run stops the backup after the current
/// file once it has uploaded that much, and the next run carries on (#7264)
/// </summary>
public class MaxUploadPerRunTests : BasicSetupHelper
{
    private const int FileCount = 40;

    private void WriteSourceFiles()
    {
        var rng = new Random(42);
        var data = new byte[64 * 1024];
        for (var i = 0; i < FileCount; i++)
        {
            rng.NextBytes(data);
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"file{i}"), data);
        }
    }

    private async Task<IBackupResults> BackupAsync(string? maxUpload)
    {
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["dblock-size"] = "100kb",
            ["blocksize"] = "4kb",
        };
        if (maxUpload != null)
            options["max-upload-per-run"] = maxUpload;

        using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
        var results = await c.BackupAsync(new[] { this.DATAFOLDER });
        Assert.That(results.Errors, Is.Empty);
        return results;
    }

    private async Task<int> RestoredFileCountAsync()
    {
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["restore-path"] = this.RESTOREFOLDER,
            ["blocksize"] = "4kb",
        };
        using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
        var results = await c.RestoreAsync(new[] { "*" });
        Assert.That(results.Errors, Is.Empty);
        return Directory.EnumerateFiles(this.RESTOREFOLDER, "*", SearchOption.AllDirectories).Count();
    }

    [Test]
    [Category("Options")]
    [TestCase(null, 0L)]
    [TestCase("", 0L)]
    [TestCase("300kb", 300L * 1024)]
    [TestCase("2", 2L * 1024 * 1024)]
    public void TheLimitIsReadAsASize(string? value, long expected)
    {
        var options = new Dictionary<string, string?>();
        if (value != null)
            options["max-upload-per-run"] = value;
        Assert.That(new Options(options).MaxUploadPerRun, Is.EqualTo(expected));
    }

    [Test]
    [Category("Targeted")]
    public async Task ABackupStopsOnceItHasUploadedTheLimitAsync()
    {
        WriteSourceFiles();

        var results = await BackupAsync("500kb");

        Assert.That(results.PartialBackup, Is.True, "the backup should stop before it has backed up every file");
        Assert.That(results.Warnings.Count(x => x.Contains("max-upload-per-run")), Is.EqualTo(1), string.Join("; ", results.Warnings));
        // The source is 2.5 MB of random data, so a full backup uploads far more than this
        Assert.That(results.BackendStatistics.BytesUploaded, Is.LessThan(2L * 1024 * 1024));
        Assert.That(await RestoredFileCountAsync(), Is.LessThan(FileCount));
    }

    [Test]
    [Category("Targeted")]
    public async Task TheNextRunsBackUpTheRestAsync()
    {
        WriteSourceFiles();

        var runs = 0;
        IBackupResults results;
        do
        {
            results = await BackupAsync("1mb");
            runs++;
        } while (results.PartialBackup && runs < 10);

        Assert.That(results.PartialBackup, Is.False, "the runs should finish the backup");
        Assert.That(runs, Is.GreaterThan(1), "the first run should have stopped at the limit");
        Assert.That(await RestoredFileCountAsync(), Is.EqualTo(FileCount));
    }

    [Test]
    [Category("Targeted")]
    public async Task WithoutTheOptionTheBackupIsNotStoppedAsync()
    {
        WriteSourceFiles();

        var results = await BackupAsync(null);

        Assert.That(results.PartialBackup, Is.False);
        Assert.That(results.Warnings.Count(x => x.Contains("max-upload-per-run")), Is.Zero);
        Assert.That(await RestoredFileCountAsync(), Is.EqualTo(FileCount));
    }
}
