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
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A failed upload is retried under a new name. A dlist name has no random part, only
/// the time, so the new name is one second later and can be the name of another dlist.
/// </summary>
public class RenameAfterErrorTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new { no_encryption = true });

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

    /// <summary>
    /// The files each remote dlist file lists, in the order of the dlist names.
    /// </summary>
    private string[] DescribeRemote()
        => DlistFiles.Select(x =>
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(TARGETFOLDER, x));
            using var rd = new StreamReader(zip.GetEntry("filelist.json")!.Open());
            var json = rd.ReadToEnd();
            return string.Join(" ", Regex.Matches(json, @"[abc]\.txt").Select(m => m.Value).Distinct().OrderBy(m => m, StringComparer.Ordinal));
        }).ToArray();

    private async Task<string> DumpAsync()
    {
        using var con = await SQLiteLoader.LoadConnectionAsync(DBFILE);
        var lines = new List<string>();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = @"SELECT ""ID"", ""Name"", ""State"" FROM ""Remotevolume"" WHERE ""Type"" = 'Files' ORDER BY ""ID""";
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                lines.Add($"volume {rd.GetInt64(0)} {rd.GetString(1)} {rd.GetString(2)}");
        }
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = @"SELECT ""ID"", ""Timestamp"", ""VolumeID"" FROM ""Fileset"" ORDER BY ""ID""";
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                lines.Add($"fileset {rd.GetInt64(0)} {DateTimeOffset.FromUnixTimeSeconds(rd.GetInt64(1)).UtcDateTime:HH:mm:ss} volume {rd.GetInt64(2)}");
        }
        string remote;
        try { remote = string.Join(" | ", DescribeRemote()); }
        catch (Exception ex) { remote = ex.Message; }
        lines.Add($"remote contents: {remote}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string WithTime(string name, DateTime time)
        => Regex.Replace(name, @"\d{8}T\d{6}Z", time.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'"));

    /// <summary>
    /// Moves the older of two filesets, and its dlist file, to the given number of seconds before the newer.
    /// </summary>
    private async Task SetFilesetGapAsync(int seconds)
    {
        using var con = await SQLiteLoader.LoadConnectionAsync(DBFILE);
        var rows = new List<(long FilesetId, long Timestamp, long VolumeId, string Name)>();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = @"SELECT ""Fileset"".""ID"", ""Fileset"".""Timestamp"", ""Remotevolume"".""ID"", ""Remotevolume"".""Name"" FROM ""Fileset"" JOIN ""Remotevolume"" ON ""Remotevolume"".""ID"" = ""Fileset"".""VolumeID"" ORDER BY ""Fileset"".""Timestamp""";
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                rows.Add((rd.GetInt64(0), rd.GetInt64(1), rd.GetInt64(2), rd.GetString(3)));
        }
        Assert.That(rows.Count, Is.EqualTo(2));

        var (older, newer) = (rows[0], rows[1]);
        var moved = newer.Timestamp - seconds;
        Assert.That(moved, Is.GreaterThanOrEqualTo(older.Timestamp));
        var name = WithTime(older.Name, DateTimeOffset.FromUnixTimeSeconds(moved).UtcDateTime);

        async Task ExecAsync(string sql, params (string Name, object Value)[] args)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in args)
                cmd.Parameters.AddWithValue(n, v);
            await cmd.ExecuteNonQueryAsync();
        }

        await ExecAsync(@"UPDATE ""Fileset"" SET ""Timestamp"" = @Ts WHERE ""ID"" = @Id", ("@Ts", moved), ("@Id", older.FilesetId));
        await ExecAsync(@"UPDATE ""Remotevolume"" SET ""Name"" = @Name WHERE ""ID"" = @Id", ("@Name", name), ("@Id", older.VolumeId));
        if (name != older.Name)
            File.Move(Path.Combine(TARGETFOLDER, older.Name), Path.Combine(TARGETFOLDER, name));
    }

    /// <summary>
    /// Two versions <paramref name="seconds"/> apart: a.txt and b.txt, then also c.txt.
    /// </summary>
    private async Task<string> SetUpTwoVersionsAsync(int seconds)
    {
        var file = Path.Combine(DATAFOLDER, "a.txt");
        File.WriteAllText(file, "a");
        File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "b");
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        Thread.Sleep(2100);
        File.WriteAllText(Path.Combine(DATAFOLDER, "c.txt"), "c");
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        await SetFilesetGapAsync(seconds);
        return file;
    }

    /// <summary>
    /// Runs the operation with the first upload of a name that <paramref name="failName"/> accepts failing, then checks
    /// that each version still has its own dlist file, before and after a database recreate.
    /// </summary>
    private async Task RunWithOneFailedUploadAsync(Func<string, bool> failName, Func<Library.Main.Controller, Task> operation, string[] expectedContents)
    {
        var before = await GetFilesetsAsync();
        var namesBefore = DlistFiles;

        Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
        var target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
        var failed = false;
        DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
        {
            if (!failed && action == DeterministicErrorBackend.BackendAction.PutBefore && failName(remotename))
                return failed = true;
            return false;
        };

        Exception? operationError = null;
        try
        {
            var options = new Dictionary<string, string>(Options)
            {
                ["number-of-retries"] = "2",
                ["retry-delay"] = "0s",
                ["no-auto-compact"] = "true",
            };
            using var c = new Library.Main.Controller(target, options, null);
            await operation(c);
        }
        catch (Exception ex)
        {
            operationError = ex;
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }

        var report = $"dlist before: {string.Join(", ", namesBefore)}{Environment.NewLine}dlist after:  {string.Join(", ", DlistFiles)}{Environment.NewLine}error: {operationError?.GetType().Name}: {operationError?.Message}{Environment.NewLine}{await DumpAsync()}";
        Assert.That(failed, Is.True, $"No upload failed{Environment.NewLine}{report}");

        // A rename after a failed upload leaves a placeholder for the failed name, which the
        // next verification of the destination removes
        Exception? cleanupError = null;
        try
        {
            using var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null);
            TestUtils.AssertResults(await c.RepairAsync());
        }
        catch (Exception ex)
        {
            cleanupError = ex;
        }
        var after = await GetFilesetsAsync();
        string[] contents;
        try { contents = DescribeRemote(); }
        catch (Exception ex) { contents = [ex.Message]; }
        report += $"{Environment.NewLine}repair after: {cleanupError?.GetType().Name}: {cleanupError?.Message}{Environment.NewLine}versions before: {Describe(before)}{Environment.NewLine}versions after:  {Describe(after)}";

        // A database recreated from the destination must have the same versions
        Exception? recreateError = null;
        (long Version, DateTime Time, string IsFull)[] recreated = [];
        try
        {
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
                TestUtils.AssertResults(await c.RepairAsync());
            recreated = await GetFilesetsAsync();
        }
        catch (Exception ex)
        {
            recreateError = ex;
        }
        report += $"{Environment.NewLine}recreate: {recreateError?.GetType().Name}: {recreateError?.Message}{Environment.NewLine}versions recreated: {Describe(recreated)}";

        Assert.That(operationError, Is.Null, report);
        Assert.That(cleanupError, Is.Null, report);
        Assert.That(after.Length, Is.EqualTo(2), report);
        // Each version in a dlist file of its own. The retried file is later than its first
        // name, so the files are compared regardless of their order.
        Assert.That(contents, Is.EquivalentTo(expectedContents), report);
        Assert.That(recreateError, Is.Null, report);
        // A dlist file that does not record its fileset time is read back at the time of its
        // name, which the retry moved, so only the versions themselves are compared.
        Assert.That(recreated.Length, Is.EqualTo(after.Length), report);
        Assert.That(recreated.Select(x => x.IsFull), Is.EqualTo(after.Select(x => x.IsFull)), report);
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeRetryDoesNotTakeTheNameOfTheNewerDlistAsync()
    {
        var file = await SetUpTwoVersionsAsync(2);

        // The purge rewrites the older version first, one second after its old name, which
        // is one second before the newer version's name. Fail that upload once.
        var older = DlistFiles[0];
        var rewritten = WithTime(older, Library.Main.Volumes.VolumeBase.ParseFilename(older).Time.AddSeconds(1));
        await RunWithOneFailedUploadAsync(name => name == rewritten,
            async c => TestUtils.AssertResults(await c.PurgeFilesAsync(new Library.Utility.FilterExpression(file))),
            ["b.txt", "b.txt c.txt"]);
    }

    [Test]
    [Category("Targeted")]
    [TestCase(1)]
    [TestCase(2)]
    public async Task RepairRetryDoesNotTakeTheNameOfTheNewerDlistAsync(int seconds)
    {
        await SetUpTwoVersionsAsync(seconds);

        // The repair uploads a replacement for the missing older dlist, and the retry of a
        // failed upload is one second later, which can be the newer version's name. Fail the
        // first upload of the replacement, whatever name the repair gave it.
        File.Delete(Path.Combine(TARGETFOLDER, DlistFiles[0]));
        await RunWithOneFailedUploadAsync(name => name.Contains(".dlist.", StringComparison.Ordinal),
            async c => TestUtils.AssertResults(await c.RepairAsync()),
            ["a.txt b.txt", "a.txt b.txt c.txt"]);
    }

    /// <summary>
    /// A backup whose dlist upload fails, and fails again under the next second, leaves its
    /// fileset with a dlist one second after the fileset time. The next backup uploads a
    /// synthetic dlist for it under the fileset time, so a retry of that upload one second
    /// later is the name of the interrupted backup's dlist.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task SyntheticFilelistRetryDoesNotTakeTheNameOfTheInterruptedDlistAsync()
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "a");
        Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
        var target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
        var options = new Dictionary<string, string>(Options)
        {
            ["number-of-retries"] = "1",
            ["retry-delay"] = "0s",
            ["no-auto-compact"] = "true",
        };

        var interruptedPuts = new List<string>();
        DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
        {
            if (action != DeterministicErrorBackend.BackendAction.PutBefore || !remotename.Contains(".dlist.", StringComparison.Ordinal))
                return false;
            interruptedPuts.Add(remotename);
            return true;
        };
        try
        {
            using var c = new Library.Main.Controller(target, options, null);
            Assert.ThrowsAsync<DeterministicErrorBackend.DeterministicErrorBackendException>(async () => await c.BackupAsync([DATAFOLDER]));
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }
        var report = $"interrupted backup:{Environment.NewLine}{await DumpAsync()}";
        Assert.That(interruptedPuts, Has.Count.EqualTo(2), report);
        var interruptedName = interruptedPuts[1];
        Thread.Sleep(3100);

        // Fail the first upload of the synthetic dlist once
        var puts = new List<string>();
        var failed = false;
        DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
        {
            if (action != DeterministicErrorBackend.BackendAction.PutBefore || !remotename.Contains(".dlist.", StringComparison.Ordinal))
                return false;
            puts.Add(remotename);
            return !failed && (failed = true);
        };
        Exception? backupError = null;
        try
        {
            using var c = new Library.Main.Controller(target, new Dictionary<string, string>(options) { ["number-of-retries"] = "2" }, null);
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        }
        catch (Exception ex)
        {
            backupError = ex;
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }
        report += $"{Environment.NewLine}uploads: {string.Join(", ", puts)}{Environment.NewLine}error: {backupError?.GetType().Name}: {backupError?.Message}{Environment.NewLine}{await DumpAsync()}";

        // The test only reaches the clash if the retry one second after the synthetic dlist's
        // first name would be the interrupted backup's dlist name
        Assert.That(puts, Has.Count.GreaterThanOrEqualTo(2), report);
        Assert.That(WithTime(puts[0], Library.Main.Volumes.VolumeBase.ParseFilename(puts[0]).Time.AddSeconds(1)), Is.EqualTo(interruptedName), report);

        // Without the check, the rename of the volume after the failed upload hits the
        // interrupted backup's entry, and the backup fails its verification of the destination
        Assert.That(backupError, Is.Null, report);

        // A rename after a failed upload leaves a placeholder for the failed name, which the
        // next verification of the destination removes
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.RepairAsync());
        var after = await GetFilesetsAsync();
        report += $"{Environment.NewLine}versions after: {Describe(after)}";
        // The synthetic version and the new backup
        Assert.That(after, Has.Length.EqualTo(2), report);

        File.Delete(DBFILE);
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.RepairAsync());
        var recreated = await GetFilesetsAsync();
        report += $"{Environment.NewLine}versions recreated: {Describe(recreated)}";
        Assert.That(recreated.Select(x => x.IsFull), Is.EqualTo(after.Select(x => x.IsFull)), report);
    }
}
