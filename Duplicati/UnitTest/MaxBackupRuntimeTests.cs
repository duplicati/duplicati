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
/// A large backup, such as the first one, kept the backups queued after it from running until it
/// was done, so --max-backup-runtime stops the backup after the current file once it has run that
/// long, and the next run carries on (#2341)
/// </summary>
public class MaxBackupRuntimeTests : BasicSetupHelper
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

    private async Task<IBackupResults> BackupAsync(string? maxRuntime, string? throttleUpload, Dictionary<string, string>? extraOptions = null)
    {
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["dblock-size"] = "100kb",
            ["blocksize"] = "4kb",
        };
        foreach (var kv in extraOptions ?? [])
            options[kv.Key] = kv.Value;
        if (maxRuntime != null)
            options["max-backup-runtime"] = maxRuntime;
        // Slows the backup down, so it runs for much longer than the limit: with one upload at a
        // time and no volumes waiting, the files are processed only as fast as they are uploaded
        if (throttleUpload != null)
        {
            options["throttle-upload"] = throttleUpload;
            options["asynchronous-upload-limit"] = "1";
            options["asynchronous-concurrent-upload-limit"] = "1";
        }

        using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
        var results = await c.BackupAsync(new[] { this.DATAFOLDER });
        Assert.That(results.Errors, Is.Empty);
        return results;
    }

    private async Task<int> RestoredFileCountAsync()
    {
        if (Directory.Exists(this.RESTOREFOLDER))
            Directory.Delete(this.RESTOREFOLDER, true);
        Directory.CreateDirectory(this.RESTOREFOLDER);

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["restore-path"] = this.RESTOREFOLDER,
            ["blocksize"] = "4kb",
        };
        using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
        var results = await c.RestoreAsync(new[] { "*" });
        Assert.That(results.Errors, Is.Empty, string.Join("; ", results.Errors));
        return Directory.EnumerateFiles(this.RESTOREFOLDER, "*", SearchOption.AllDirectories).Count();
    }

    [Test]
    [Category("Options")]
    [TestCase(null, 0)]
    [TestCase("", 0)]
    [TestCase("90", 90)]
    [TestCase("2h", 2 * 60 * 60)]
    [TestCase("1D", 24 * 60 * 60)]
    public void TheLimitIsReadAsATimespan(string? value, int expectedSeconds)
    {
        var options = new Dictionary<string, string?>();
        if (value != null)
            options["max-backup-runtime"] = value;
        Assert.That(new Options(options).MaxBackupRuntime, Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
    }

    [Test]
    [Category("Targeted")]
    public async Task ABackupStopsOnceItHasRunForTheLimitAndTheNextRunBacksUpTheRestAsync()
    {
        WriteSourceFiles();

        // The source is 2.5 MB of random data, so at 100 KB/s a full backup takes over 25 seconds,
        // and the limit leaves time for the checks before the backup on a slow machine
        var results = await BackupAsync("10s", "100kb");

        Assert.That(results.ExaminedFiles, Is.GreaterThan(0), "the limit was reached before the files were processed, so the checks before the backup took longer than the limit");
        Assert.That(results.PartialBackup, Is.True, "the backup should stop before it has backed up every file");
        Assert.That(results.Warnings.Count(x => x.Contains("max-backup-runtime")), Is.EqualTo(1), string.Join("; ", results.Warnings));
        Assert.That(await RestoredFileCountAsync(), Is.LessThan(FileCount));

        results = await BackupAsync(null, null);

        Assert.That(results.PartialBackup, Is.False);
        Assert.That(await RestoredFileCountAsync(), Is.EqualTo(FileCount));
    }

    [Test]
    [Category("Targeted")]
    public async Task ABackupThatEndsBeforeTheLimitIsNotStoppedAsync()
    {
        WriteSourceFiles();

        var results = await BackupAsync("1h", null);

        Assert.That(results.PartialBackup, Is.False);
        Assert.That(results.Warnings.Count(x => x.Contains("max-backup-runtime")), Is.Zero);
        Assert.That(await RestoredFileCountAsync(), Is.EqualTo(FileCount));
    }

    [Test]
    [Category("Targeted")]
    public async Task ALimitReachedAfterTheFilesDoesNotStopTheVerificationAsync()
    {
        WriteSourceFiles();

        // The files are processed at full speed, well within the limit, while the verification
        // after the backup downloads its samples slowly, so the limit is reached during it
        const int Samples = 1;
        var results = await BackupAsync("12s", null, new Dictionary<string, string>
        {
            ["backup-test-samples"] = Samples.ToString(),
            ["throttle-download"] = "5kb",
        });

        Assert.That(results.ExaminedFiles, Is.EqualTo(FileCount), "the limit was reached before every file was processed, so the backup took longer than the limit");
        Assert.That(results.Duration, Is.GreaterThan(TimeSpan.FromSeconds(12)), "the verification should take longer than the limit");
        Assert.That(results.PartialBackup, Is.False);
        Assert.That(results.Warnings.Count(x => x.Contains("max-backup-runtime")), Is.Zero, string.Join("; ", results.Warnings));

        // Each sample is a block volume and an index file, and the one file list is checked too
        var verifications = ((BackupResults)results).TestResults.Verifications.ToList();
        Assert.That(verifications.Count, Is.EqualTo(Samples * 2 + 1), string.Join("; ", verifications.Select(x => x.Key)));
        Assert.That(await RestoredFileCountAsync(), Is.EqualTo(FileCount));
    }
}
