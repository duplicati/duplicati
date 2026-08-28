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
    /// Points the metadata of the named file at the shared empty-file blockset, which is the state
    /// reported in the wild: a metadata entry with a length of 0, sharing its blockset with every empty
    /// file in the backup.
    /// </summary>
    /// <returns>The metadata id that was repointed.</returns>
    private static async Task<long> PointMetadataAtSharedEmptyBlocksetAsync(SqliteCommand cmd, string path, long sharedEmptyBlocksetId)
    {
        var metadataId = cmd
            .SetCommandAndParameters(@"SELECT ""MetadataID"" FROM ""File"" WHERE ""Path"" LIKE @Path")
            .SetParameterValue("@Path", "%" + path)
            .ExecuteScalarInt64(-1);

        Assert.That(metadataId, Is.GreaterThanOrEqualTo(0), $"No metadata found for {path}");

        var updated = await cmd
            .SetCommandAndParameters(@"UPDATE ""Metadataset"" SET ""BlocksetID"" = @BlocksetId WHERE ""ID"" = @Id")
            .SetParameterValue("@BlocksetId", sharedEmptyBlocksetId)
            .SetParameterValue("@Id", metadataId)
            .ExecuteNonQueryAsync();

        Assert.That(updated, Is.EqualTo(1), "Metadata was not repointed");
        return metadataId;
    }

    /// <summary>
    /// Counts the metadata entries that cannot be restored because their blockset has a length of 0.
    /// </summary>
    private static long CountZeroLengthMetadata(SqliteCommand cmd)
        => cmd.ExecuteScalarInt64(@"
            SELECT COUNT(*)
            FROM ""Metadataset""
            JOIN ""Blockset"" ON ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID""
            WHERE ""Blockset"".""Length"" = 0
        ", -1);

    /// <summary>
    /// Returns the id of the blockset holding the given metadata blob, and asserts that it is backed by
    /// blocks in a live block volume, which is what makes the replacement survive a database recreate.
    /// </summary>
    private static long GetStoredMetadataBlocksetId(SqliteCommand cmd, IMetahash metadata)
    {
        var blocksetId = cmd
            .SetCommandAndParameters(@"SELECT ""ID"" FROM ""Blockset"" WHERE ""FullHash"" = @Hash AND ""Length"" = @Length")
            .SetParameterValue("@Hash", metadata.FileHash)
            .SetParameterValue("@Length", metadata.Blob.Length)
            .ExecuteScalarInt64(-1);

        Assert.That(blocksetId, Is.GreaterThanOrEqualTo(0), "The empty metadata blockset is not registered");

        var blocksInLiveVolumes = cmd
            .SetCommandAndParameters(@"
                SELECT COUNT(*)
                FROM ""BlocksetEntry""
                JOIN ""Block"" ON ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
                JOIN ""RemoteVolume"" ON ""RemoteVolume"".""ID"" = ""Block"".""VolumeID""
                WHERE
                    ""BlocksetEntry"".""BlocksetID"" = @Id
                    AND ""RemoteVolume"".""Type"" = 'Blocks'
                    AND ""RemoteVolume"".""State"" NOT IN ('Deleted', 'Deleting', 'Temporary')
            ")
            .SetParameterValue("@Id", blocksetId)
            .ExecuteScalarInt64(0);

        Assert.That(blocksInLiveVolumes, Is.EqualTo(1), "The empty metadata blockset must be backed by a stored block");
        return blocksetId;
    }

    /// <summary>
    /// Returns the blockset the given metadata entry points at.
    /// </summary>
    private static long GetMetadataBlocksetId(SqliteCommand cmd, long metadataId)
        => cmd
            .SetCommandAndParameters(@"SELECT ""BlocksetID"" FROM ""Metadataset"" WHERE ""ID"" = @Id")
            .SetParameterValue("@Id", metadataId)
            .ExecuteScalarInt64(-1);

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

    [Test]
    [Category("Targeted")]
    public async Task ListBrokenFilesReportsUnrestorableMetadataAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
            await PointMetadataAtSharedEmptyBlocksetAsync(cmd, "a.txt", GetSharedEmptyBlocksetId(cmd, emptyFileHash));

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var res = await c.ListBrokenFilesAsync(null);
            Assert.That(res.Errors.Count(), Is.EqualTo(0));

            var broken = res.BrokenFiles.SelectMany(x => x.Item3).Select(x => x.Item1).ToArray();
            Assert.That(broken.Length, Is.EqualTo(1), "Expected exactly one broken file, got: " + string.Join(", ", broken));
            Assert.That(broken[0], Does.EndWith("a.txt"), "The file with the unrestorable metadata should be reported");
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeRecoversUnrestorableMetadataAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);

        long metadataId;
        long emptyBlocksetId;
        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            emptyBlocksetId = GetSharedEmptyBlocksetId(cmd, emptyFileHash);
            metadataId = await PointMetadataAtSharedEmptyBlocksetAsync(cmd, "a.txt", emptyBlocksetId);
        }

        var dlistsBefore = Directory.GetFiles(this.TARGETFOLDER, "*.dlist.*").Length;

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var res = await c.PurgeBrokenFilesAsync(null);
            Assert.That(res.Errors.Count(), Is.EqualTo(0));
            Assert.That(res.Warnings.Count(x => x.Contains("MetadataReplacedWithEmpty")), Is.EqualTo(1), "The recovery should be reported");
            TestUtils.AssertResults(res, "MetadataReplacedWithEmpty");

            Assert.That(res.DeleteResults, Is.Null, "No fileset should have been deleted");
            Assert.That(res.PurgeResults?.RemovedFileCount, Is.EqualTo(0), "No file should have been removed");
            Assert.That(res.PurgeResults?.RewrittenFileLists, Is.EqualTo(1), "The filelist must be rewritten, or the replacement is not persisted");
        }

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            Assert.That(GetMetadataBlocksetId(cmd, metadataId), Is.Not.EqualTo(emptyBlocksetId), "The metadata should point at a restorable blockset");
            Assert.That(
                cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""Metadataset"" JOIN ""Blockset"" ON ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" WHERE ""Blockset"".""Length"" = 0", -1),
                Is.EqualTo(0),
                "No metadata entry may have a length of 0");
        }

        // The empty file keeps the shared blockset, and both files are still in the backup
        Assert.That(Directory.GetFiles(this.TARGETFOLDER, "*.dlist.*").Length, Is.EqualTo(dlistsBefore), "The old filelist should have been replaced");

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var files = (await c.ListAsync("*")).Files.Select(x => x.Path).ToArray();
            Assert.That(files.Count(x => x.EndsWith("a.txt")), Is.EqualTo(1), "The recovered file should still be in the backup");
            Assert.That(files.Count(x => x.EndsWith("empty.txt")), Is.EqualTo(1), "The empty file should not have been touched");

            TestUtils.AssertResults(await c.TestAsync(int.MaxValue));
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeRemovesFileWhenReplacementIsDisabledAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        options["disable-replace-missing-metadata"] = "true";

        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
            await PointMetadataAtSharedEmptyBlocksetAsync(cmd, "a.txt", GetSharedEmptyBlocksetId(cmd, emptyFileHash));

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var res = await c.PurgeBrokenFilesAsync(null);
            Assert.That(res.Errors.Count(), Is.EqualTo(0));
            Assert.That(res.Warnings.Count(x => x.Contains("MetadataReplacedWithEmpty")), Is.EqualTo(0), "Nothing should have been recovered");
            Assert.That(res.PurgeResults?.RemovedFileCount, Is.EqualTo(1), "The file should have been removed");
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var files = (await c.ListAsync("*")).Files.Select(x => x.Path).ToArray();
            Assert.That(files.Any(x => x.EndsWith("a.txt")), Is.False, "The file should have been removed");
            Assert.That(files.Count(x => x.EndsWith("empty.txt")), Is.EqualTo(1), "The empty file should not have been touched");
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task ScopedPurgeDoesNotTouchSharedMetadataAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);

        // A second version that keeps the unchanged file, and therefore shares its metadata row
        Thread.Sleep(2000);
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "b.txt"), "more data");
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

        long metadataId;
        long emptyBlocksetId;
        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            emptyBlocksetId = GetSharedEmptyBlocksetId(cmd, emptyFileHash);
            metadataId = await PointMetadataAtSharedEmptyBlocksetAsync(cmd, "a.txt", emptyBlocksetId);

            Assert.That(
                cmd.SetCommandAndParameters(@"SELECT COUNT(DISTINCT ""FilesetID"") FROM ""FilesetEntry"" JOIN ""FileLookup"" ON ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FileLookup"".""MetadataID"" = @Id")
                    .SetParameterValue("@Id", metadataId)
                    .ExecuteScalarInt64(0),
                Is.EqualTo(2),
                "Both versions should share the damaged metadata row");
        }

        // Only version 0 gets a new filelist, so repointing the shared row would leave version 1
        // referencing the old metadata, and a later recreate would reintroduce the damage
        var scopedOptions = new Dictionary<string, string>(options) { ["version"] = "0" };
        using (var c = new Controller("file://" + this.TARGETFOLDER, scopedOptions, null))
        {
            var res = await c.PurgeBrokenFilesAsync(null);
            Assert.That(res.Errors.Count(), Is.EqualTo(0));
            Assert.That(res.Warnings.Count(x => x.Contains("ReplaceableMetadataOutsideSelection")), Is.EqualTo(1), "The skipped replacement should be reported");
            Assert.That(res.Warnings.Count(x => x.Contains("MetadataReplacedWithEmpty")), Is.EqualTo(0), "Nothing should have been recovered");

            // Without the replacement there is nothing to recover the file with, so it is removed from
            // the selected version, which is what the warning says happens
            Assert.That(res.PurgeResults?.RemovedFileCount, Is.EqualTo(1), "The affected file should have been removed from the selected version");
        }

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
            Assert.That(GetMetadataBlocksetId(cmd, metadataId), Is.EqualTo(emptyBlocksetId), "The shared metadata row must not have been repointed");

        using (var c = new Controller("file://" + this.TARGETFOLDER, scopedOptions, null))
        {
            var newest = (await c.ListAsync("*")).Files.Select(x => x.Path).ToArray();
            Assert.That(newest.Any(x => x.EndsWith("a.txt")), Is.False, "The affected file should be gone from the purged version");
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(options) { ["version"] = "1" }, null))
        {
            var older = (await c.ListAsync("*")).Files.Select(x => x.Path).ToArray();
            Assert.That(older.Count(x => x.EndsWith("a.txt")), Is.EqualTo(1), "The unselected version should be untouched");
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeStoresReplacementMetadataAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);
        var emptyMetadata = Duplicati.Library.Main.Utility.WrapMetadata(new Dictionary<string, string>(), opts);

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
            await PointMetadataAtSharedEmptyBlocksetAsync(cmd, "a.txt", GetSharedEmptyBlocksetId(cmd, emptyFileHash));

        var dblocksBefore = Directory.GetFiles(this.TARGETFOLDER, "*.dblock.*").Length;

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var res = await c.PurgeBrokenFilesAsync(null);
            TestUtils.AssertResults(res, "MetadataReplacedWithEmpty");
            Assert.That(res.PurgeResults?.RewrittenFileLists, Is.EqualTo(1));
        }

        // The empty metadata blob is not part of a normal backup, so it has to be stored now
        Assert.That(Directory.GetFiles(this.TARGETFOLDER, "*.dblock.*").Length, Is.EqualTo(dblocksBefore + 1), "The empty metadata blob should have been stored in a new block volume");

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            GetStoredMetadataBlocksetId(cmd, emptyMetadata);
            Assert.That(CountZeroLengthMetadata(cmd), Is.EqualTo(0), "No metadata entry may have a length of 0");
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.TestAsync(int.MaxValue));

        // The replacement is only real if it is recorded at the destination: recreating the database from
        // the remote files must not bring the damage back
        File.Delete(this.DBFILE);
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RepairAsync());

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            Assert.That(CountZeroLengthMetadata(cmd), Is.EqualTo(0), "The recreated database must not have zero-length metadata");
            GetStoredMetadataBlocksetId(cmd, emptyMetadata);
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            TestUtils.AssertResults(await c.TestAsync(int.MaxValue));

            var files = (await c.ListAsync("*")).Files.Select(x => x.Path).ToArray();
            Assert.That(files.Count(x => x.EndsWith("a.txt")), Is.EqualTo(1), "The recovered file should still be in the backup");
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeRewritesEveryAffectedFilelistAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);
        var emptyMetadata = Duplicati.Library.Main.Utility.WrapMetadata(new Dictionary<string, string>(), opts);

        // A second version that keeps the unchanged file, and therefore shares its metadata row
        Thread.Sleep(2000);
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "b.txt"), "more data");
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

        long metadataId;
        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            metadataId = await PointMetadataAtSharedEmptyBlocksetAsync(cmd, "a.txt", GetSharedEmptyBlocksetId(cmd, emptyFileHash));

            Assert.That(
                cmd.SetCommandAndParameters(@"SELECT COUNT(DISTINCT ""FilesetID"") FROM ""FilesetEntry"" JOIN ""FileLookup"" ON ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FileLookup"".""MetadataID"" = @Id")
                    .SetParameterValue("@Id", metadataId)
                    .ExecuteScalarInt64(0),
                Is.EqualTo(2),
                "Both versions should share the damaged metadata row");
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var res = await c.PurgeBrokenFilesAsync(null);
            TestUtils.AssertResults(res, "MetadataReplacedWithEmpty");

            // Leaving one filelist behind is what makes the damage come back on the next recreate
            Assert.That(res.PurgeResults?.RewrittenFileLists, Is.EqualTo(2), "Every affected filelist must be rewritten");
            Assert.That(res.PurgeResults?.RemovedFileCount, Is.EqualTo(0), "No file should have been removed");
        }

        File.Delete(this.DBFILE);
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RepairAsync());

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            Assert.That(CountZeroLengthMetadata(cmd), Is.EqualTo(0), "The recreated database must not have zero-length metadata");
            GetStoredMetadataBlocksetId(cmd, emptyMetadata);
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.TestAsync(int.MaxValue));
    }

    [Test]
    [Category("Targeted")]
    public async Task DryrunPurgeDoesNotChangeAnythingAsync()
    {
        var options = await BackupWithEmptyFileAsync();
        var opts = new Options(options);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);

        long metadataId;
        long emptyBlocksetId;
        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
        {
            emptyBlocksetId = GetSharedEmptyBlocksetId(cmd, emptyFileHash);
            metadataId = await PointMetadataAtSharedEmptyBlocksetAsync(cmd, "a.txt", emptyBlocksetId);
        }

        var remoteBefore = Directory.GetFiles(this.TARGETFOLDER).OrderBy(x => x).ToArray();
        var databaseBefore = File.ReadAllBytes(this.DBFILE);

        var dryrunOptions = new Dictionary<string, string>(options) { ["dry-run"] = "true" };
        using (var c = new Controller("file://" + this.TARGETFOLDER, dryrunOptions, null))
        {
            var res = await c.PurgeBrokenFilesAsync(null);
            Assert.That(res.Warnings.Count(x => x.Contains("MetadataReplacedWithEmpty")), Is.EqualTo(1), "The dry-run should report what it would recover");
            // Nothing was repaired, so the closing consistency check still finds the damage. A real run
            // does not report this, because by then the metadata has been replaced.
            Assert.That(res.Warnings.Count(x => x.Contains("ZeroLengthMetadata")), Is.EqualTo(1), "The dry-run should still report the unrestorable metadata");
            TestUtils.AssertResults(res, "MetadataReplacedWithEmpty", "ZeroLengthMetadata");
        }

        // A dry-run must not store the replacement, and must not rewrite any filelist
        Assert.That(Directory.GetFiles(this.TARGETFOLDER).OrderBy(x => x).ToArray(), Is.EqualTo(remoteBefore), "A dry-run must not change the destination");
        Assert.That(File.ReadAllBytes(this.DBFILE), Is.EqualTo(databaseBefore), "A dry-run must not change the database");

        using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        using (var cmd = con.CreateCommand())
            Assert.That(GetMetadataBlocksetId(cmd, metadataId), Is.EqualTo(emptyBlocksetId), "A dry-run must not repoint the metadata");
    }
}
