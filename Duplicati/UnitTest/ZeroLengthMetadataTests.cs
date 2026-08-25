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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.SQLiteHelper;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for the repair operations around the shared empty-file blockset.
/// </summary>
/// <remarks>
/// <c>Blockset</c> is unique on <c>("FullHash", "Length")</c>, so there is at most one zero-length
/// blockset, and it is the blockset that every empty file in the backup uses as its content. Blocks
/// attached to that one row therefore make the consistency check fail for every empty file at once.
/// </remarks>
public class ZeroLengthMetadataTests : BasicSetupHelper
{
    /// <summary>
    /// Creates a backup containing a regular file and an empty file, so that the shared empty-file
    /// blockset exists, and returns the options used.
    /// </summary>
    private async Task<Dictionary<string, string>> BackupWithEmptyFileAsync()
    {
        var options = new Dictionary<string, string>(this.TestOptions);
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "a.txt"), "some data");
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "empty.txt"), string.Empty);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

        return options;
    }

    /// <summary>
    /// Returns the ID of the shared empty-file blockset, asserting that it exists.
    /// </summary>
    private static long GetSharedEmptyBlocksetId(SqliteCommand cmd, string emptyFileHash)
    {
        var id = cmd
            .SetCommandAndParameters(@"SELECT ""ID"" FROM ""Blockset"" WHERE ""Length"" = 0 AND ""FullHash"" = @Hash")
            .SetParameterValue("@Hash", emptyFileHash)
            .ExecuteScalarInt64(-1);

        Assert.That(id, Is.GreaterThanOrEqualTo(0), "The shared empty-file blockset was not found");
        return id;
    }

    /// <summary>
    /// Counts the blockset entries attached to a blockset.
    /// </summary>
    private static long CountBlocksetEntries(SqliteCommand cmd, long blocksetId)
        => cmd
            .SetCommandAndParameters(@"SELECT COUNT(*) FROM ""BlocksetEntry"" WHERE ""BlocksetID"" = @Id")
            .SetParameterValue("@Id", blocksetId)
            .ExecuteScalarInt64(0);

    /// <summary>
    /// Attaches an existing block to a blockset, simulating the damage seen in the wild.
    /// </summary>
    private static async Task AttachBlockAsync(SqliteCommand cmd, long blocksetId)
    {
        var blockId = cmd.ExecuteScalarInt64(@"SELECT ""ID"" FROM ""Block"" ORDER BY ""ID"" LIMIT 1", -1);
        Assert.That(blockId, Is.GreaterThanOrEqualTo(0), "No blocks in the backup");

        await cmd
            .SetCommandAndParameters(@"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") VALUES (@BlocksetId, 0, @BlockId)")
            .SetParameterValue("@BlocksetId", blocksetId)
            .SetParameterValue("@BlockId", blockId)
            .ExecuteNonQueryAsync();
    }

    [Test]
    [Category("RepairHandler")]
    public async Task RepairStripsBlocksFromSharedEmptyBlocksetAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);

        long emptyBlocksetId;
        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            emptyBlocksetId = GetSharedEmptyBlocksetId(cmd, emptyFileHash);
            await AttachBlockAsync(cmd, emptyBlocksetId);
        }

        // The empty file now claims a length of 0 while referencing a block, which the consistency
        // check reports for every empty file sharing the blockset.
        Assert.ThrowsAsync<DatabaseInconsistencyException>(async () =>
        {
            await using var db = await LocalDatabase.CreateLocalDatabaseAsync(this.DBFILE, "verify", true, null, CancellationToken.None);
            await db.VerifyConsistencyAsync(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None);
        });

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var res = await c.RepairAsync();
            Assert.That(res.Errors.Count(), Is.EqualTo(0));
            Assert.That(res.Warnings.Count(), Is.EqualTo(0));
        }

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
            Assert.That(CountBlocksetEntries(cmd, emptyBlocksetId), Is.EqualTo(0), "Blocks should have been stripped from the shared empty blockset");

        await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(this.DBFILE, "verify", true, null, CancellationToken.None))
            await db.VerifyConsistencyAsync(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None);
    }

    [Test]
    [Category("RepairHandler")]
    public async Task RepairRemovesOrphanedBlocksetEntriesAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);

        long orphanBlocksetId;
        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            // A blockset ID that does not exist; the rowid may be handed out again by a later insert,
            // which is what makes these rows dangerous rather than merely untidy.
            orphanBlocksetId = cmd.ExecuteScalarInt64(@"SELECT IFNULL(MAX(""ID""), 0) + 1000 FROM ""Blockset""", -1);
            await AttachBlockAsync(cmd, orphanBlocksetId);

            await cmd
                .SetCommandAndParameters(@"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (@Id, 0, 'orphan-blocklist-hash')")
                .SetParameterValue("@Id", orphanBlocksetId)
                .ExecuteNonQueryAsync();
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var res = await c.RepairAsync();
            Assert.That(res.Errors.Count(), Is.EqualTo(0));
            Assert.That(res.Warnings.Count(), Is.EqualTo(0));
        }

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            Assert.That(CountBlocksetEntries(cmd, orphanBlocksetId), Is.EqualTo(0), "Orphaned blockset entries should have been removed");
            Assert.That(
                cmd.SetCommandAndParameters(@"SELECT COUNT(*) FROM ""BlocklistHash"" WHERE ""BlocksetID"" = @Id")
                    .SetParameterValue("@Id", orphanBlocksetId)
                    .ExecuteScalarInt64(0),
                Is.EqualTo(0),
                "Orphaned blocklist hashes should have been removed");
        }

        await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(this.DBFILE, "verify", true, null, CancellationToken.None))
            await db.VerifyConsistencyAsync(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None);
    }

    [Test]
    [Category("RepairHandler")]
    public async Task RepairReportsButDoesNotEmptyFalselyZeroLengthBlocksetAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);

        long emptyBlocksetId;
        long corruptBlocksetId;
        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            emptyBlocksetId = GetSharedEmptyBlocksetId(cmd, emptyFileHash);
            await AttachBlockAsync(cmd, emptyBlocksetId);

            // A file blockset that keeps its blocks and its real hash, but is falsely marked as
            // zero-length. Stripping its blocks would turn a broken file into one that silently
            // restores as empty, so it must be reported instead.
            corruptBlocksetId = cmd.ExecuteScalarInt64(@"
                SELECT ""B"".""ID""
                FROM ""Blockset"" ""B""
                JOIN ""FileLookup"" ""F"" ON ""F"".""BlocksetID"" = ""B"".""ID""
                WHERE ""B"".""Length"" > 0
                LIMIT 1
            ", -1);
            Assert.That(corruptBlocksetId, Is.GreaterThanOrEqualTo(0), "No file blockset found");

            await cmd
                .SetCommandAndParameters(@"UPDATE ""Blockset"" SET ""Length"" = 0 WHERE ""ID"" = @Id")
                .SetParameterValue("@Id", corruptBlocksetId)
                .ExecuteNonQueryAsync();
        }

        await using (var db = await LocalRepairDatabase.CreateRepairDatabaseAsync(this.DBFILE, null, CancellationToken.None))
        {
            // Only the shared empty blockset is emptied, even though both now claim a length of 0.
            var stripped = await db.FixEmptyBlocksetWithBlocksAsync(emptyFileHash, false, CancellationToken.None);
            Assert.That(stripped, Is.EqualTo(1), "Only the shared empty blockset should have been stripped");

            var mismatches = await db.ReportBlocksetLengthMismatchesAsync(emptyFileHash, CancellationToken.None);
            Assert.That(mismatches, Is.EqualTo(1), "The falsely zero-length blockset should be reported as unrepairable");

            await db.Transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            Assert.That(CountBlocksetEntries(cmd, emptyBlocksetId), Is.EqualTo(0), "Blocks should have been stripped from the shared empty blockset");
            Assert.That(CountBlocksetEntries(cmd, corruptBlocksetId), Is.GreaterThan(0), "The falsely zero-length blockset must keep its blocks");
        }

        // Repair cannot fix this state, and must not pretend otherwise: the consistency check that
        // guards the remote repair still refuses to proceed.
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            Assert.ThrowsAsync<DatabaseInconsistencyException>(() => c.RepairAsync());

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
            Assert.That(CountBlocksetEntries(cmd, corruptBlocksetId), Is.GreaterThan(0), "The falsely zero-length blockset must keep its blocks");
    }

    [Test]
    [Category("RepairHandler")]
    public async Task DryrunDoesNotChangeTheDatabaseAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);

        long emptyBlocksetId;
        long orphanBlocksetId;
        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            emptyBlocksetId = GetSharedEmptyBlocksetId(cmd, emptyFileHash);
            await AttachBlockAsync(cmd, emptyBlocksetId);

            orphanBlocksetId = cmd.ExecuteScalarInt64(@"SELECT IFNULL(MAX(""ID""), 0) + 1000 FROM ""Blockset""", -1);
            await AttachBlockAsync(cmd, orphanBlocksetId);
        }

        await using (var db = await LocalRepairDatabase.CreateRepairDatabaseAsync(this.DBFILE, null, CancellationToken.None))
        {
            Assert.That(await db.FixOrphanBlocksetEntriesAsync(true, CancellationToken.None), Is.EqualTo(1));
            Assert.That(await db.FixEmptyBlocksetWithBlocksAsync(emptyFileHash, true, CancellationToken.None), Is.EqualTo(1));

            await db.Transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            Assert.That(CountBlocksetEntries(cmd, emptyBlocksetId), Is.EqualTo(1), "A dry-run must not strip blocks");
            Assert.That(CountBlocksetEntries(cmd, orphanBlocksetId), Is.EqualTo(1), "A dry-run must not remove orphaned entries");
        }
    }
}
