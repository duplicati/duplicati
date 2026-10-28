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
/// A dlist file that is missing from the destination is uploaded again under its own
/// name. Any other free name is at least a second later, and with two filesets one
/// second apart that is past the newer one's file.
/// </summary>
public class RepairDlistNameTests : BasicSetupHelper
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

    private async Task RunBackupAsync(string filename)
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, filename), filename);
        using var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null);
        TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
    }

    private async Task RunRepairAsync(string target, Dictionary<string, string> options)
    {
        using var c = new Library.Main.Controller(target, options, null);
        TestUtils.AssertResults(await c.RepairAsync());
    }

    private static string Describe((long Version, DateTime Time, string IsFull)[] filesets)
        => string.Join(" | ", filesets.Select(x => $"v{x.Version} {x.Time:O} full={x.IsFull}"));

    private static string WithTime(string name, DateTime time)
        => Regex.Replace(name, @"\d{8}T\d{6}Z", time.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'"));

    /// <summary>
    /// The dlist names of the filesets, oldest fileset first.
    /// </summary>
    private async Task<string[]> GetDlistNamesByFilesetTimeAsync()
    {
        using var con = await SQLiteLoader.LoadConnectionAsync(DBFILE);
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT ""Remotevolume"".""Name"" FROM ""Fileset"" JOIN ""Remotevolume"" ON ""Remotevolume"".""ID"" = ""Fileset"".""VolumeID"" ORDER BY ""Fileset"".""Timestamp""";
        var names = new List<string>();
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            names.Add(rd.GetString(0));
        return names.ToArray();
    }

    /// <summary>
    /// Moves the older of two filesets, and its dlist file, to one second before the newer,
    /// which is how close two backups can be.
    /// </summary>
    private async Task SqueezeFilesetsAsync()
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
        var squeezed = newer.Timestamp - 1;
        var name = WithTime(older.Name, DateTimeOffset.FromUnixTimeSeconds(squeezed).UtcDateTime);

        async Task ExecAsync(string sql, params (string Name, object Value)[] args)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in args)
                cmd.Parameters.AddWithValue(n, v);
            await cmd.ExecuteNonQueryAsync();
        }

        await ExecAsync(@"UPDATE ""Fileset"" SET ""Timestamp"" = @Ts WHERE ""ID"" = @Id", ("@Ts", squeezed), ("@Id", older.FilesetId));
        await ExecAsync(@"UPDATE ""Remotevolume"" SET ""Name"" = @Name WHERE ""ID"" = @Id", ("@Name", name), ("@Id", older.VolumeId));
        if (name != older.Name)
            File.Move(Path.Combine(TARGETFOLDER, older.Name), Path.Combine(TARGETFOLDER, name));
    }

    [Test]
    [Category("Targeted")]
    public async Task RepairOfTheOlderDlistFileKeepsTheOrderAsync()
    {
        await RunBackupAsync("a.txt");
        Thread.Sleep(1100);
        await RunBackupAsync("b.txt");
        await SqueezeFilesetsAsync();

        var namesBefore = DlistFiles;
        File.Delete(Path.Combine(TARGETFOLDER, namesBefore[0]));
        await RunRepairAsync("file://" + TARGETFOLDER, Options);

        var names = await GetDlistNamesByFilesetTimeAsync();
        Assert.That(names, Is.EqualTo(names.OrderBy(x => x, StringComparer.Ordinal).ToArray()),
            $"After the repair, the dlist names are not in the order of the filesets they describe (oldest fileset first): {string.Join(", ", names)}");
        Assert.That(DlistFiles, Is.EqualTo(namesBefore));
    }

    [Test]
    [Category("Targeted")]
    public async Task RepairUploadsMissingDlistFilesUnderTheirOwnNamesAsync()
    {
        await RunBackupAsync("a.txt");
        Thread.Sleep(1100);
        await RunBackupAsync("b.txt");

        var before = await GetFilesetsAsync();
        var namesBefore = DlistFiles;
        foreach (var f in Directory.GetFiles(TARGETFOLDER, "*.dlist.*"))
            File.Delete(f);

        await RunRepairAsync("file://" + TARGETFOLDER, Options);

        Assert.That(DlistFiles, Is.EqualTo(namesBefore));
        var after = await GetFilesetsAsync();
        Assert.That(after, Is.EqualTo(before),
            $"Repair changed the versions.{Environment.NewLine}before: {Describe(before)}{Environment.NewLine}after:  {Describe(after)}");

        // The files must be readable, and a database recreated from them must agree
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
        {
            var r = await c.TestAsync(long.MaxValue);
            Assert.That(r.Errors.Count(), Is.EqualTo(0));
            Assert.That(r.Verifications.Any(p => p.Value.Any()), Is.False);
        }

        File.Delete(DBFILE);
        await RunRepairAsync("file://" + TARGETFOLDER, Options);
        var recreated = await GetFilesetsAsync();
        Assert.That(recreated, Is.EqualTo(before),
            $"Recreating the database changed the versions.{Environment.NewLine}before:   {Describe(before)}{Environment.NewLine}recreate: {Describe(recreated)}");
    }

    [Test]
    [Category("Targeted")]
    public async Task RepairRenamesTheDlistFileWhenItsUploadFailsAsync()
    {
        await RunBackupAsync("a.txt");
        Thread.Sleep(2100);
        await RunBackupAsync("b.txt");

        var before = await GetFilesetsAsync();
        var namesBefore = DlistFiles;
        var older = namesBefore[0];
        File.Delete(Path.Combine(TARGETFOLDER, older));

        Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
        var target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
        var failed = false;
        DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
        {
            if (!failed && action == DeterministicErrorBackend.BackendAction.PutBefore && remotename == older)
                return failed = true;
            return false;
        };

        try
        {
            var options = new Dictionary<string, string>(Options)
            {
                ["number-of-retries"] = "2",
                ["retry-delay"] = "0s",
            };
            await RunRepairAsync(target, options);
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }

        Assert.That(failed, Is.True, "The upload under the original name was not attempted");

        // A failed upload is retried under a name one second later, as for any other upload
        var renamed = WithTime(older, Library.Main.Volumes.VolumeBase.ParseFilename(older).Time.AddSeconds(1));
        Assert.That(DlistFiles, Is.EqualTo(new[] { renamed, namesBefore[1] }));

        // The rename leaves a placeholder for the name that failed, which the next
        // verification of the destination removes
        await RunRepairAsync("file://" + TARGETFOLDER, Options);
        var after = await GetFilesetsAsync();
        Assert.That(after, Is.EqualTo(before),
            $"Repair changed the versions.{Environment.NewLine}before: {Describe(before)}{Environment.NewLine}after:  {Describe(after)}");
    }
}
