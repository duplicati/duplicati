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
using System.Linq;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Interface;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;

namespace Duplicati.UnitTest;

public class EmptyMetadataTests : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public async Task ReplaceMissingMetadataRestoresConsistencyAsync()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });

        // Create a simple folder structure
        Directory.CreateDirectory(Path.Combine(DATAFOLDER, "folder"));
        File.WriteAllText(Path.Combine(DATAFOLDER, "folder", "file.txt"), "data");

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

        long metaBlocksetId;
        long filesetId;

        using (var db = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        using (var cmd = db.CreateCommand())
        {
            // Find metadata blockset ID and fileset ID for the backed up folder
            cmd.SetCommandAndParameters($@"SELECT M.""BlocksetID"", FE.""FilesetID""
                                         FROM ""File"" F
                                         JOIN ""FilesetEntry"" FE ON FE.""FileID"" = F.""ID""
                                         JOIN ""Metadataset"" M ON F.""MetadataID"" = M.""ID""
                                         WHERE F.""Path"" LIKE @Path AND F.""BlocksetID"" = {LocalDatabase.FOLDER_BLOCKSET_ID}
                                         ORDER BY FE.""FilesetID"" DESC LIMIT 1");
            cmd.SetParameterValue("@Path", "%folder%");
            using (var rd = await cmd.ExecuteReaderAsync())
            {
                Assert.That(await rd.ReadAsync(), Is.True, "Folder entry not found");
                metaBlocksetId = rd.ConvertValueToInt64(0);
                filesetId = rd.ConvertValueToInt64(1);
            }

            // Remove metadata blockset entries to simulate missing metadata
            cmd.SetCommandAndParameters("DELETE FROM \"BlocksetEntry\" WHERE \"BlocksetID\" = @Id");
            cmd.SetParameterValue("@Id", metaBlocksetId);
            await cmd.ExecuteNonQueryAsync();

            // Remove the now orphaned blockset record
            cmd.SetCommandAndParameters("DELETE FROM \"Blockset\" WHERE \"ID\" = @Id");
            cmd.SetParameterValue("@Id", metaBlocksetId);
            await cmd.ExecuteNonQueryAsync();
        }

        var opts = new Options(testopts);
        var emptyMeta = Duplicati.Library.Main.Utility.WrapMetadata(new Dictionary<string, string>(), opts);

        Assert.ThrowsAsync<DatabaseInconsistencyException>(async () =>
        {
            using var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "verify", true, null, CancellationToken.None);
            await db.VerifyConsistencyAsync(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None);
        });

        await using (var db = await LocalListBrokenFilesDatabase.CreateAsync(DBFILE, null, CancellationToken.None).ConfigureAwait(false))
        {
            var unavailableVolumeIds = Array.Empty<long>();

            var replacementId = await db.FindExactMetadataBlocksetIdAsync(unavailableVolumeIds, emptyMeta.FileHash, emptyMeta.Blob.Length, CancellationToken.None);
            if (replacementId < 0)
                replacementId = await db.FindSmallestUsableMetadataBlocksetIdAsync(unavailableVolumeIds, CancellationToken.None);

            Assert.That(replacementId, Is.GreaterThanOrEqualTo(0), "No replacement metadata blockset found");

            var replaced = await db.ReplaceMetadataAsync(filesetId, replacementId, CancellationToken.None);
            Assert.That(replaced, Is.GreaterThan(0), "No metadata rows replaced");

            await db.Transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "verify", true, null, CancellationToken.None))
            await db.VerifyConsistencyAsync(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None);
    }

    /// <summary>
    /// Tests that the replacement metadata lookups work with a large number of unavailable volume IDs,
    /// which triggers the temporary table code path (when count > CHUNK_SIZE = 128), and that a blockset
    /// stored in an unavailable volume is not offered as a replacement.
    /// </summary>
    [Test]
    [Category("Database")]
    public async Task FindMetadataBlocksetId_WithLargeInput_UsesTemporaryTable_Async()
    {
        const string METADATA_HASH = "bWV0YWRhdGEtYmxvY2s=";
        const string BLOCK_HASH = "bWV0YWRhdGEtYmxvY2staGFzaA==";
        const long METADATA_SIZE = 2;
        const long BLOCK_VOLUME_ID = 151;

        using var dbfile = new TempFile();
        using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);

        // Use DatabaseUpgrader to create the schema from embedded resources
        DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));

        using var cmd = db.CreateCommand();

        // Insert an operation record (required for LocalDatabase initialization)
        cmd.CommandText = @"INSERT INTO ""Operation"" (""Description"", ""Timestamp"") VALUES ('Test', 0)";
        await cmd.ExecuteNonQueryAsync();

        // Create 150 volume IDs (exceeds CHUNK_SIZE of 128) to trigger the temporary table path,
        // plus one more that holds the block the candidate blockset is made of
        var unavailableVolumeIds = new List<long>();
        for (var i = 1; i <= BLOCK_VOLUME_ID; i++)
        {
            if (i != BLOCK_VOLUME_ID)
                unavailableVolumeIds.Add(i);

            cmd.CommandText = $@"
                INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                VALUES ({i}, 1, 'block-volume-{i}.zip', 'Blocks', 'Verified', 0, 0, 0, 0)";
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.That(unavailableVolumeIds.Count, Is.GreaterThan(128), "The temporary table path is only used above the chunk size");

        // A realistic replacement candidate: a non-empty blockset, backed by a real block in an
        // available volume, that is already used as metadata
        cmd.CommandText = $@"
            INSERT INTO ""Block"" (""ID"", ""Hash"", ""Size"", ""VolumeID"") VALUES (1, '{BLOCK_HASH}', {METADATA_SIZE}, {BLOCK_VOLUME_ID});
            INSERT INTO ""Blockset"" (""ID"", ""FullHash"", ""Length"") VALUES (1, '{METADATA_HASH}', {METADATA_SIZE});
            INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") VALUES (1, 0, 1);
            INSERT INTO ""Metadataset"" (""ID"", ""BlocksetID"") VALUES (1, 1);
        ";
        await cmd.ExecuteNonQueryAsync();

        // Close the connection so LocalDatabase can open it
        await db.CloseAsync();

        await using var localDb = await LocalListBrokenFilesDatabase.CreateAsync(dbfile, null, CancellationToken.None).ConfigureAwait(false);

        Assert.That(
            await localDb.FindExactMetadataBlocksetIdAsync(unavailableVolumeIds, METADATA_HASH, METADATA_SIZE, CancellationToken.None),
            Is.EqualTo(1),
            "The exact blockset should be found when its volume is available");

        Assert.That(
            await localDb.FindSmallestUsableMetadataBlocksetIdAsync(unavailableVolumeIds, CancellationToken.None),
            Is.EqualTo(1),
            "The blockset should be usable as a fallback when its volume is available");

        // Excluding the volume that holds the block makes the blockset unrestorable, so neither lookup
        // may offer it
        var allVolumesUnavailable = unavailableVolumeIds.Append(BLOCK_VOLUME_ID).ToList();

        Assert.That(
            await localDb.FindExactMetadataBlocksetIdAsync(allVolumesUnavailable, METADATA_HASH, METADATA_SIZE, CancellationToken.None),
            Is.EqualTo(-1),
            "A blockset in an unavailable volume must not be returned");

        Assert.That(
            await localDb.FindSmallestUsableMetadataBlocksetIdAsync(allVolumesUnavailable, CancellationToken.None),
            Is.EqualTo(-1),
            "A blockset in an unavailable volume must not be returned as a fallback");
    }

    /// <summary>
    /// The shared zero-length blockset is the content of every empty file in the backup, so it must never
    /// be handed out as replacement metadata: it has no blocks and can therefore not be restored, and
    /// assigning it entangles the damaged metadata with every empty file.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task ReplacementMetadataIsNeverTheSharedEmptyBlocksetAsync()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });

        File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "data");
        File.WriteAllText(Path.Combine(DATAFOLDER, "empty.txt"), string.Empty);

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

        var opts = new Options(testopts);
        var emptyMeta = Duplicati.Library.Main.Utility.WrapMetadata(new Dictionary<string, string>(), opts);
        var emptyFileHash = Duplicati.Library.Main.Utility.CalculateEmptyFileHash(opts);

        long sharedEmptyBlocksetId;
        using (var db = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        using (var cmd = db.CreateCommand())
        {
            sharedEmptyBlocksetId = cmd
                .SetCommandAndParameters(@"SELECT ""ID"" FROM ""Blockset"" WHERE ""Length"" = 0 AND ""FullHash"" = @Hash")
                .SetParameterValue("@Hash", emptyFileHash)
                .ExecuteScalarInt64(-1);
            Assert.That(sharedEmptyBlocksetId, Is.GreaterThanOrEqualTo(0), "The shared empty-file blockset was not found");
        }

        await using var localDb = await LocalListBrokenFilesDatabase.CreateAsync(DBFILE, null, CancellationToken.None).ConfigureAwait(false);
        var unavailableVolumeIds = Array.Empty<long>();

        // The canonical empty metadata blob is not stored by a normal backup, so the exact lookup finds
        // nothing rather than falling back to the shared zero-length blockset
        Assert.That(
            await localDb.FindExactMetadataBlocksetIdAsync(unavailableVolumeIds, emptyMeta.FileHash, emptyMeta.Blob.Length, CancellationToken.None),
            Is.EqualTo(-1),
            "The empty metadata blob is not stored in this backup");

        var fallbackId = await localDb.FindSmallestUsableMetadataBlocksetIdAsync(unavailableVolumeIds, CancellationToken.None);
        Assert.That(fallbackId, Is.GreaterThanOrEqualTo(0), "No fallback metadata blockset found");
        Assert.That(fallbackId, Is.Not.EqualTo(sharedEmptyBlocksetId), "The shared empty-file blockset must never be used as metadata");

        using (var db = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        using (var cmd = db.CreateCommand())
        {
            var length = cmd
                .SetCommandAndParameters(@"SELECT ""Length"" FROM ""Blockset"" WHERE ""ID"" = @Id")
                .SetParameterValue("@Id", fallbackId)
                .ExecuteScalarInt64(-1);
            Assert.That(length, Is.GreaterThan(0), "Replacement metadata must have contents");

            var blocks = cmd
                .SetCommandAndParameters(@"SELECT COUNT(*) FROM ""BlocksetEntry"" WHERE ""BlocksetID"" = @Id")
                .SetParameterValue("@Id", fallbackId)
                .ExecuteScalarInt64(0);
            Assert.That(blocks, Is.GreaterThan(0), "Replacement metadata must have blocks to restore from");
        }
    }
}
