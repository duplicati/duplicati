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

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A backup that starts from an almost empty database leaves "sqlite_stat1" saying the tables
/// hold a row or two, and then fills them with tens of thousands. The planner keeps believing
/// the recorded numbers and picks join orders for the fileset queries that never finish.
/// Reported as issue #7127.
/// </summary>
public class QueryStatisticsTests : BasicSetupHelper
{
    /// <summary>
    /// Rewrites every recorded row count as one, which is the state a first backup of a single
    /// file leaves behind.
    /// </summary>
    private async Task MakeStatisticsDescribeOneRowAsync()
    {
        await using var con = await SQLiteLoader.LoadConnectionAsync(DBFILE);
        await using var cmd = con.CreateCommand();

        var rewritten = new List<(string Table, string Index, string Stat)>();
        cmd.SetCommandAndParameters(@"SELECT ""tbl"", ""idx"", ""stat"" FROM ""sqlite_stat1""");
        await using (var rd = await cmd.ExecuteReaderAsync())
            while (await rd.ReadAsync())
            {
                // Only the leading number is the row count; the rest describes the columns and
                // has to keep its shape for SQLite to read the row back.
                var stat = rd.ConvertValueToString(2) ?? "1";
                var space = stat.IndexOf(' ');
                rewritten.Add((rd.ConvertValueToString(0)!, rd.ConvertValueToString(1)!,
                    space < 0 ? "1" : "1" + stat.Substring(space)));
            }

        Assert.That(rewritten, Is.Not.Empty, "The backup left no statistics to work with");

        foreach (var row in rewritten)
        {
            cmd.SetCommandAndParameters(@"UPDATE ""sqlite_stat1"" SET ""stat"" = @Stat WHERE ""tbl"" = @Table AND ""idx"" IS @Index");
            cmd.SetParameterValue("@Stat", row.Stat);
            cmd.SetParameterValue("@Table", row.Table);
            cmd.SetParameterValue("@Index", row.Index);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Reads what the statistics claim a table holds, and what it actually holds.
    /// </summary>
    private async Task<(long Recorded, long Actual)> ReadStatisticsForAsync(string table)
    {
        await using var con = await SQLiteLoader.LoadConnectionAsync(DBFILE);
        await using var cmd = con.CreateCommand();

        cmd.SetCommandAndParameters(@"SELECT CAST(""stat"" AS INTEGER) FROM ""sqlite_stat1"" WHERE ""tbl"" = @Table LIMIT 1");
        cmd.SetParameterValue("@Table", table);
        var recorded = await cmd.ExecuteScalarInt64Async(-1, CancellationToken.None);

        var actual = await cmd.ExecuteScalarInt64Async($@"SELECT COUNT(*) FROM ""{table}""", 0, CancellationToken.None);
        return (recorded, actual);
    }

    private async Task BackupManyFilesAsync()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });

        // Enough files that the recorded row count and the real one are far apart once the
        // statistics are reset, which is what the refresh has to notice.
        for (var i = 0; i < 40; i++)
        {
            var dir = Path.Combine(DATAFOLDER, $"folder-{i}");
            Directory.CreateDirectory(dir);
            for (var j = 0; j < 10; j++)
                File.WriteAllText(Path.Combine(dir, $"file-{j}.txt"), $"contents {i} {j}");
        }

        using var c = new Controller("file://" + TARGETFOLDER, testopts, null);
        TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
    }

    /// <summary>
    /// The refresh has to run against the transaction the database keeps open, because a backup
    /// never leaves one. A bare "PRAGMA optimize" is silently a no-op there -- only the 0x10000
    /// bit makes it look past the tables this connection has touched -- so this fails if the mask
    /// is ever dropped, which is a change that looks harmless in a diff.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task StaleStatisticsAreRebuiltWhileATransactionIsOpenAsync()
    {
        await BackupManyFilesAsync();
        await MakeStatisticsDescribeOneRowAsync();

        var before = await ReadStatisticsForAsync("FileLookup");
        Assert.That(before.Recorded, Is.EqualTo(1), "The statistics were not reset");
        Assert.That(before.Actual, Is.GreaterThan(100), "Too few rows for the statistics to be meaningfully stale");

        await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "test", true, null, CancellationToken.None))
        {
            await db.RefreshQueryStatisticsAsync(CancellationToken.None);
            await db.Transaction.CommitAsync(CancellationToken.None);
        }

        var after = await ReadStatisticsForAsync("FileLookup");
        Assert.That(after.Recorded, Is.EqualTo(after.Actual),
            $"The statistics still describe {after.Recorded} row(s) where there are {after.Actual}");
    }

    /// <summary>
    /// Statistics that already describe the database are left alone, so an ordinary backup does
    /// not pay for an ANALYZE it does not need.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task CurrentStatisticsAreLeftAloneAsync()
    {
        await BackupManyFilesAsync();

        // Give the statistics a recognisable shape that a rebuild would replace.
        await using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        await using (var cmd = con.CreateCommand())
        {
            var (recorded, actual) = await ReadStatisticsForAsync("FileLookup");
            Assert.That(recorded, Is.GreaterThan(0), "The backup left no statistics to work with");
            Assert.That(recorded * 10, Is.GreaterThan(actual), "The statistics are already stale, so nothing is being tested");

            cmd.SetCommandAndParameters(@"UPDATE ""sqlite_stat1"" SET ""stat"" = ""stat"" || ' 1' WHERE ""tbl"" = 'FileLookup'");
            await cmd.ExecuteNonQueryAsync();
        }

        string marked;
        await using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        await using (var cmd = con.CreateCommand())
            marked = (await cmd.ExecuteScalarAsync(@"SELECT ""stat"" FROM ""sqlite_stat1"" WHERE ""tbl"" = 'FileLookup' LIMIT 1", CancellationToken.None))?.ToString() ?? "";

        await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "test", true, null, CancellationToken.None))
        {
            await db.RefreshQueryStatisticsAsync(CancellationToken.None);
            await db.Transaction.CommitAsync(CancellationToken.None);
        }

        await using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        await using (var cmd = con.CreateCommand())
        {
            var now = (await cmd.ExecuteScalarAsync(@"SELECT ""stat"" FROM ""sqlite_stat1"" WHERE ""tbl"" = 'FileLookup' LIMIT 1", CancellationToken.None))?.ToString() ?? "";
            Assert.That(now, Is.EqualTo(marked), "The statistics were rebuilt even though they described the database");
        }
    }
}
