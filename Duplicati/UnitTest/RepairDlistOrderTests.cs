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
/// A repaired dlist file gets the first free name at or after its fileset time, and
/// every name it skips is one that is already taken. Two filesets one second apart
/// leave no room, so the replacement names can end up in the opposite order of the
/// filesets they describe, and the fileset time must not be taken from such a name.
/// </summary>
public class RepairDlistOrderTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new { no_encryption = true });

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

    private async Task RunRepairAsync()
    {
        using var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null);
        TestUtils.AssertResults(await c.RepairAsync());
    }

    private static string Describe((long Version, DateTime Time, string IsFull)[] filesets)
        => string.Join(" | ", filesets.Select(x => $"v{x.Version} {x.Time:O} full={x.IsFull}"));

    /// <summary>
    /// The number of entries in each fileset, newest fileset first, which tells the two backups apart.
    /// </summary>
    private async Task<long[]> GetEntryCountsAsync()
    {
        using var con = await SQLiteLoader.LoadConnectionAsync(DBFILE);
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT (SELECT COUNT(*) FROM ""FilesetEntry"" WHERE ""FilesetEntry"".""FilesetID"" = ""Fileset"".""ID"") FROM ""Fileset"" ORDER BY ""Fileset"".""Timestamp"" DESC";
        var counts = new List<long>();
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            counts.Add(rd.GetInt64(0));
        return counts.ToArray();
    }

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
    /// which is how close two backups can be. With <paramref name="newerFirst"/> the newer
    /// fileset's volume is also listed first, as the database gives no order to the volumes.
    /// </summary>
    private async Task SqueezeFilesetsAsync(bool newerFirst)
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
        var name = Regex.Replace(older.Name, @"\d{8}T\d{6}Z", DateTimeOffset.FromUnixTimeSeconds(squeezed).UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'"));

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

        if (newerFirst)
        {
            await ExecAsync(@"UPDATE ""Remotevolume"" SET ""ID"" = (SELECT MAX(""ID"") + 1 FROM ""Remotevolume"") WHERE ""ID"" = @Id", ("@Id", older.VolumeId));
            await ExecAsync(@"UPDATE ""Fileset"" SET ""VolumeID"" = (SELECT ""ID"" FROM ""Remotevolume"" WHERE ""Name"" = @Name) WHERE ""ID"" = @Id", ("@Name", name), ("@Id", older.FilesetId));
        }
    }

    private async Task SetUpTwoSqueezedBackupsAsync(bool newerFirst)
    {
        await RunBackupAsync("a.txt");
        Thread.Sleep(1100);
        await RunBackupAsync("b.txt");
        await SqueezeFilesetsAsync(newerFirst);
    }

    private static void AssertNamesFollowFilesets(string[] names, string when)
        => Assert.That(names, Is.EqualTo(names.OrderBy(x => x, StringComparer.Ordinal).ToArray()),
            $"{when}, the dlist names are not in the order of the filesets they describe (oldest fileset first): {string.Join(", ", names)}");

    [Test]
    [Category("Targeted")]
    [TestCase(false)]
    [TestCase(true)]
    public async Task RepairOfAllDlistFilesKeepsTheirOrderAsync(bool newerFirst)
    {
        await SetUpTwoSqueezedBackupsAsync(newerFirst);
        AssertNamesFollowFilesets(await GetDlistNamesByFilesetTimeAsync(), "Before the repair");

        foreach (var f in Directory.GetFiles(TARGETFOLDER, "*.dlist.*"))
            File.Delete(f);
        await RunRepairAsync();

        AssertNamesFollowFilesets(await GetDlistNamesByFilesetTimeAsync(), "After the repair");
    }

    [Test]
    [Category("Targeted")]
    public async Task SecondRepairKeepsTheVersionsAsync()
    {
        await SetUpTwoSqueezedBackupsAsync(true);
        var before = await GetFilesetsAsync();
        var countsBefore = await GetEntryCountsAsync();

        for (var i = 0; i < 2; i++)
        {
            foreach (var f in Directory.GetFiles(TARGETFOLDER, "*.dlist.*"))
                File.Delete(f);
            await RunRepairAsync();

            var after = await GetFilesetsAsync();
            Assert.That(await GetEntryCountsAsync(), Is.EqualTo(countsBefore),
                $"Repair {i + 1} swapped the backups between the versions.{Environment.NewLine}before: {Describe(before)}{Environment.NewLine}after:  {Describe(after)}");
            Assert.That(after, Is.EqualTo(before),
                $"Repair {i + 1} changed the versions.{Environment.NewLine}before: {Describe(before)}{Environment.NewLine}after:  {Describe(after)}");
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task RecreateAfterRepairKeepsTheVersionsAsync()
    {
        await SetUpTwoSqueezedBackupsAsync(true);
        var before = await GetFilesetsAsync();

        foreach (var f in Directory.GetFiles(TARGETFOLDER, "*.dlist.*"))
            File.Delete(f);
        await RunRepairAsync();

        File.Delete(DBFILE);
        await RunRepairAsync();

        var after = await GetFilesetsAsync();
        Assert.That(after, Is.EqualTo(before),
            $"Recreating the database changed the versions.{Environment.NewLine}before:   {Describe(before)}{Environment.NewLine}recreate: {Describe(after)}");
    }
}
