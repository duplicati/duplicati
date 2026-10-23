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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A failed upload is retried under a later name, and a dlist name is the time of its
/// fileset. A database recreated from the destination must still have the fileset's time,
/// not the time of the name the retry gave the file.
/// </summary>
public class DlistFilesetTimeTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new { no_encryption = true });

    private Dictionary<string, string> RetryOptions => new(Options)
    {
        ["number-of-retries"] = "2",
        ["retry-delay"] = "0s",
        ["no-auto-compact"] = "true",
    };

    private string[] DlistFiles
        => Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly)
            .Select(x => Path.GetFileName(x))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private async Task<(long Version, DateTime Time, string IsFull)[]> GetFilesetsAsync()
    {
        using var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null);
        var result = await c.ListAsync();
        return result.Filesets
            .OrderBy(x => x.Version)
            .Select(x => ((long)x.Version, x.Time, x.IsFullBackup.ToString()))
            .ToArray();
    }

    private static string Describe((long Version, DateTime Time, string IsFull)[] filesets)
        => string.Join(" | ", filesets.Select(x => $"v{x.Version} {x.Time:O} full={x.IsFull}"));

    private static string WithTime(string name, DateTime time)
        => Regex.Replace(name, @"\d{8}T\d{6}Z", time.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'"));

    /// <summary>
    /// Runs the operation against a destination where the first upload of a name that
    /// <paramref name="failName"/> accepts fails.
    /// </summary>
    private async Task RunWithOneFailedUploadAsync(Func<string, bool> failName, Func<Library.Main.Controller, Task> operation)
    {
        Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
        var target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
        var failed = false;
        DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
        {
            if (!failed && action == DeterministicErrorBackend.BackendAction.PutBefore && failName(remotename))
                return failed = true;
            return false;
        };

        try
        {
            using var c = new Library.Main.Controller(target, RetryOptions, null);
            await operation(c);
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }

        Assert.That(failed, Is.True, $"No upload failed; dlist files: {string.Join(", ", DlistFiles)}");
    }

    /// <summary>
    /// Recreates the database from the destination and checks that the versions are the same.
    /// </summary>
    private async Task AssertRecreateKeepsTheVersionsAsync()
    {
        // A rename after a failed upload leaves a placeholder for the failed name, which the
        // next verification of the destination removes
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.RepairAsync());
        var before = await GetFilesetsAsync();

        File.Delete(DBFILE);
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.RepairAsync());
        var recreated = await GetFilesetsAsync();

        Assert.That(recreated, Is.EqualTo(before),
            $"Recreating the database changed the versions.{Environment.NewLine}dlist files: {string.Join(", ", DlistFiles)}{Environment.NewLine}before:   {Describe(before)}{Environment.NewLine}recreate: {Describe(recreated)}");
    }

    [Test]
    [Category("Targeted")]
    public async Task BackupRetryKeepsTheFilesetTimeAcrossARecreateAsync()
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "a");
        await RunWithOneFailedUploadAsync(name => name.Contains(".dlist.", StringComparison.Ordinal),
            async c => TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER])));

        await AssertRecreateKeepsTheVersionsAsync();
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeRetryKeepsTheFilesetTimeAcrossARecreateAsync()
    {
        var file = Path.Combine(DATAFOLDER, "a.txt");
        File.WriteAllText(file, "a");
        File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "b");
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        Thread.Sleep(3100);
        File.WriteAllText(Path.Combine(DATAFOLDER, "c.txt"), "c");
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        // The purge rewrites the older version one second after its old name. Fail that
        // upload once, so it is retried a second later still.
        var older = DlistFiles[0];
        var rewritten = WithTime(older, Library.Main.Volumes.VolumeBase.ParseFilename(older).Time.AddSeconds(1));
        await RunWithOneFailedUploadAsync(name => name == rewritten,
            async c => TestUtils.AssertResults(await c.PurgeFilesAsync(new Library.Utility.FilterExpression(file))));

        await AssertRecreateKeepsTheVersionsAsync();
    }

    /// <summary>
    /// Runs a backup whose dlist upload fails without a retry, so the next backup uploads a
    /// synthetic dlist for it.
    /// </summary>
    private async Task InterruptBackupAsync()
    {
        Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
        var target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
        DeterministicErrorBackend.ErrorGenerator = (action, remotename)
            => action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dlist.", StringComparison.Ordinal);

        try
        {
            using var c = new Library.Main.Controller(target, new Dictionary<string, string>(Options) { ["number-of-retries"] = "0" }, null);
            Assert.ThrowsAsync<DeterministicErrorBackend.DeterministicErrorBackendException>(async () => await c.BackupAsync([DATAFOLDER]));
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task SyntheticFilelistRetryKeepsTheFilesetTimeAcrossARecreateAsync()
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "a");
        await InterruptBackupAsync();
        Thread.Sleep(3100);

        // The synthetic dlist is the first dlist the next backup uploads, and it has the
        // time of the interrupted backup, not of the backup that uploads it
        var started = DateTime.UtcNow.AddSeconds(-1);
        string? failedName = null;
        await RunWithOneFailedUploadAsync(name =>
            {
                if (!name.Contains(".dlist.", StringComparison.Ordinal))
                    return false;
                failedName = name;
                return true;
            },
            async c => TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER])));
        Assert.That(Library.Main.Volumes.VolumeBase.ParseFilename(failedName!).Time, Is.LessThan(started),
            $"The failed upload was not the synthetic dlist: {failedName}");

        await AssertRecreateKeepsTheVersionsAsync();
    }
}
