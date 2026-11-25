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
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A blockset with a length but no blocks stops every backup with "Detected non-empty blocksets
/// with no associated blocks!". One was left when a file could not be added to the database,
/// and repair could not remove it, as no file used it.
/// </summary>
public class Issue3594 : BasicSetupHelper
{
    /// <summary>
    /// Counts the blocksets that the consistency check stops at.
    /// </summary>
    private async Task<long> CountBlocksetsWithoutBlocksAsync()
    {
        await using var con = await SQLiteLoader.LoadConnectionAsync(DBFILE);
        await using var cmd = con.CreateCommand();
        return await cmd.ExecuteScalarInt64Async(@"
            SELECT COUNT(*)
            FROM ""Blockset""
            WHERE
                ""Length"" > 0
                AND ""ID"" NOT IN (
                    SELECT ""BlocksetID""
                    FROM ""BlocksetEntry""
                )
        ", 0, CancellationToken.None);
    }

    [Test]
    [Category("Targeted")]
    public async Task AFileThatCannotBeAddedLeavesNoBlocksetAsync()
    {
        var options = new Options(new Dictionary<string, string>(TestOptions) { ["blocksize"] = "1kb" });
        await using (var db = await LocalBackupDatabase.CreateAsync(DBFILE, options, CancellationToken.None))
        {
            var volume = await db.RegisterRemoteVolumeAsync("duplicati-b1.dblock.zip", RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, CancellationToken.None);
            await db.AddBlockAsync("A", 1024, volume, CancellationToken.None);
            await db.AddBlockAsync("B", 10, volume, CancellationToken.None);

            // The first block of the file is not in the database
            Assert.ThrowsAsync<Exception>(async () =>
                await db.AddBlocksetAsync("F1", 1034, 1024, ["MISSING", "A"], ["BL"], CancellationToken.None));

            // The next file commits the transaction that the files share
            var (added, _) = await db.AddBlocksetAsync("F2", 10, 1024, ["B"], null, CancellationToken.None);
            Assert.That(added, Is.True);
        }

        Assert.That(await CountBlocksetsWithoutBlocksAsync(), Is.EqualTo(0), "The file that could not be added left its blockset");
    }

    [Test]
    [Category("Targeted")]
    public async Task RepairRemovesABlocksetThatNoFileUsesAsync()
    {
        var options = new Dictionary<string, string>(TestOptions);
        var path = Path.Combine(DATAFOLDER, "a.txt");
        File.WriteAllText(path, "some data");
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        // As in the report: a length, no blocks, and no file that uses it
        await using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        await using (var cmd = con.CreateCommand())
            await cmd.ExecuteNonQueryAsync(@"
                INSERT INTO ""Blockset"" (""Length"", ""FullHash"")
                VALUES (6646562, 'LRFelwMN6lAdLHz+NSK6yNqz/vW5e7l2WfibCVFoQXQ=')
            ", CancellationToken.None);

        File.WriteAllText(path, "other data");
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            Assert.ThrowsAsync<DatabaseInconsistencyException>(async () => await c.BackupAsync([DATAFOLDER]),
                "The blockset should stop the backup, as in the issue");

            TestUtils.AssertResults(await c.RepairAsync());
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        }

        using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(options) { ["restore-path"] = RESTOREFOLDER }, null))
            TestUtils.AssertResults(await c.RestoreAsync([path]));
        Assert.That(File.ReadAllText(Path.Combine(RESTOREFOLDER, "a.txt")), Is.EqualTo("other data"));
    }

    [Test]
    [Category("Targeted")]
    public async Task RepairKeepsABlocksetWithoutBlocksThatIsUsedAsync()
    {
        var options = new Dictionary<string, string>(TestOptions);
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "some data");
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        long used;
        await using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        await using (var cmd = con.CreateCommand())
        {
            // The blockset of the file loses its blocks, and another one that nothing uses is added
            used = await cmd.ExecuteScalarInt64Async(@"
                SELECT ""BlocksetID""
                FROM ""FileLookup""
                WHERE ""BlocksetID"" >= 0
                    AND ""BlocksetID"" IN (SELECT ""ID"" FROM ""Blockset"" WHERE ""Length"" > 0)
                LIMIT 1
            ", -1, CancellationToken.None);
            Assert.That(used, Is.GreaterThanOrEqualTo(0));
            await cmd.SetCommandAndParameters(@"DELETE FROM ""BlocksetEntry"" WHERE ""BlocksetID"" = @Id")
                .SetParameterValue("@Id", used)
                .ExecuteNonQueryAsync(CancellationToken.None);
            await cmd.ExecuteNonQueryAsync(@"
                INSERT INTO ""Blockset"" (""Length"", ""FullHash"")
                VALUES (100, 'unused')
            ", CancellationToken.None);
        }

        await using (var db = await LocalRepairDatabase.CreateRepairDatabaseAsync(DBFILE, null, CancellationToken.None))
            Assert.That(await db.FixUnusedBlocksetsWithoutBlocksAsync(false, CancellationToken.None), Is.EqualTo(1));

        await using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        await using (var cmd = con.CreateCommand())
        {
            Assert.That(await cmd.SetCommandAndParameters(@"SELECT COUNT(*) FROM ""Blockset"" WHERE ""ID"" = @Id")
                .SetParameterValue("@Id", used)
                .ExecuteScalarInt64Async(0, CancellationToken.None), Is.EqualTo(1), "The blockset of the file is for purge-broken-files to remove");
            Assert.That(await cmd.ExecuteScalarInt64Async(@"SELECT COUNT(*) FROM ""Blockset"" WHERE ""FullHash"" = 'unused'", 0, CancellationToken.None), Is.EqualTo(0));
        }
    }
}
