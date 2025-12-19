// Copyright (C) 2025, The Duplicati Team
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

using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for LocalDatabase.RemoveRemoteVolumes method, specifically targeting
    /// the constraint exception at line 1250 that detects orphaned FilesetEntry records.
    /// </summary>
    [TestFixture]
    public class LocalDatabaseRemoveRemoteVolumesTests
    {
        /// <summary>
        /// Tests that RemoveRemoteVolumes throws a ConstraintException when there are
        /// FilesetEntry records that reference FileIDs not present in FileLookup.
        ///
        /// This test directly inserts an orphaned FilesetEntry record (one that references
        /// a FileID that doesn't exist in FileLookup) and verifies that the validation
        /// at line 1250 catches it.
        ///
        /// Note: This test simulates a database corruption scenario where the normal
        /// deletion logic has somehow left behind orphaned records.
        /// </summary>
        [Test]
        [Category("Database")]
        public async Task RemoveRemoteVolumes_WithOrphanedFilesetEntry_ThrowsConstraintException()
        {
            using var dbfile = new TempFile();
            using var db = SQLiteLoader.LoadConnection(dbfile);

            // Use DatabaseUpgrader to create the schema from embedded resources
            DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));

            using var cmd = db.CreateCommand();

            // Insert an operation record (required for LocalDatabase initialization)
            cmd.CommandText = @"INSERT INTO ""Operation"" (""Description"", ""Timestamp"") VALUES ('Test', 0)";
            cmd.ExecuteNonQuery();

            // Insert a block volume (ID=1) - this is the volume we will delete
            cmd.CommandText = @"
                INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                VALUES (1, 1, 'block-volume.zip', 'Blocks', 'Verified', 0, 0, 0, 0)";
            cmd.ExecuteNonQuery();

            // Insert a fileset volume (ID=2) - this volume is NOT being deleted
            cmd.CommandText = @"
                INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                VALUES (2, 1, 'fileset-volume.zip', 'Files', 'Verified', 0, 0, 0, 0)";
            cmd.ExecuteNonQuery();

            // Insert a fileset that references the fileset volume (NOT being deleted)
            cmd.CommandText = @"
                INSERT INTO ""Fileset"" (""ID"", ""OperationID"", ""VolumeID"", ""IsFullBackup"", ""Timestamp"")
                VALUES (1, 1, 2, 1, 0)";
            cmd.ExecuteNonQuery();

            // Insert an orphaned FilesetEntry - this references a FileID (99999) that does NOT exist in FileLookup
            // This simulates a database corruption scenario
            cmd.CommandText = @"
                INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"")
                VALUES (1, 99999, 0)";
            cmd.ExecuteNonQuery();

            // Close the connection so LocalDatabase can open it
            db.Close();

            // Create LocalDatabase instance and attempt to remove the block volume
            await using var localDb = await LocalDatabase.CreateLocalDatabaseAsync(
                dbfile,
                "TestOperation",
                true,
                null,
                CancellationToken.None
            );

            // Act & Assert: RemoveRemoteVolumes should throw ConstraintException
            // The validation at line 1250 checks for FilesetEntry records that reference
            // FileIDs not present in FileLookup. Since we inserted an orphaned record,
            // this should trigger the exception.
            ConstraintException? caughtException = null;
            try
            {
                await localDb.RemoveRemoteVolumes(new[] { "block-volume.zip" }, CancellationToken.None);
            }
            catch (ConstraintException ex)
            {
                caughtException = ex;
            }

            Assert.That(caughtException, Is.TypeOf<ConstraintException>());
            Assert.That(caughtException!.Message, Does.Contain("FilesetEntry"));
            Assert.That(caughtException.Message, Does.Contain("FileLookup"));
        }

        /// <summary>
        /// Tests that RemoveRemoteVolumes succeeds when all FilesetEntry records
        /// have corresponding FileLookup entries (no orphans).
        /// </summary>
        [Test]
        [Category("Database")]
        public async Task RemoveRemoteVolumes_WithValidFilesetEntry_Succeeds()
        {
            using var dbfile = new TempFile();
            using var db = SQLiteLoader.LoadConnection(dbfile);

            // Use DatabaseUpgrader to create the schema from embedded resources
            DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));

            using var cmd = db.CreateCommand();

            // Insert an operation record
            cmd.CommandText = @"INSERT INTO ""Operation"" (""Description"", ""Timestamp"") VALUES ('Test', 0)";
            cmd.ExecuteNonQuery();

            // Insert a remote volume that we will try to remove
            cmd.CommandText = @"
                INSERT INTO ""Remotevolume"" (""OperationID"", ""Name"", ""Type"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                VALUES (1, 'test-volume.zip', 'Blocks', 'Verified', 0, 0, 0, 0)";
            cmd.ExecuteNonQuery();

            // Close the connection so LocalDatabase can open it
            db.Close();

            // Create LocalDatabase instance and attempt to remove the volume
            await using var localDb = await LocalDatabase.CreateLocalDatabaseAsync(
                dbfile,
                "TestOperation",
                true,
                null,
                CancellationToken.None
            );

            // Act & Assert: RemoveRemoteVolumes should succeed without throwing
            // (no FilesetEntry records exist, so no orphans can be created)
            Assert.DoesNotThrowAsync(async () =>
            {
                await localDb.RemoveRemoteVolumes(new[] { "test-volume.zip" }, CancellationToken.None);
            });
        }

        /// <summary>
        /// Tests that RemoveRemoteVolumes throws a ConstraintException with the correct count
        /// when multiple FilesetEntry records are orphaned.
        ///
        /// This test directly inserts multiple orphaned FilesetEntry records and verifies
        /// that the validation at line 1250 catches them and reports the correct count.
        /// </summary>
        [Test]
        [Category("Database")]
        public async Task RemoveRemoteVolumes_WithMultipleOrphanedFilesetEntries_ThrowsConstraintExceptionWithCount()
        {
            using var dbfile = new TempFile();
            using var db = SQLiteLoader.LoadConnection(dbfile);

            // Use DatabaseUpgrader to create the schema from embedded resources
            DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));

            using var cmd = db.CreateCommand();

            // Insert an operation record
            cmd.CommandText = @"INSERT INTO ""Operation"" (""Description"", ""Timestamp"") VALUES ('Test', 0)";
            cmd.ExecuteNonQuery();

            // Insert a block volume (ID=1) - this is the volume we will delete
            cmd.CommandText = @"
                INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                VALUES (1, 1, 'block-volume.zip', 'Blocks', 'Verified', 0, 0, 0, 0)";
            cmd.ExecuteNonQuery();

            // Insert a fileset volume (ID=2) - this volume is NOT being deleted
            cmd.CommandText = @"
                INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                VALUES (2, 1, 'fileset-volume.zip', 'Files', 'Verified', 0, 0, 0, 0)";
            cmd.ExecuteNonQuery();

            // Insert a fileset that references the fileset volume (NOT being deleted)
            cmd.CommandText = @"
                INSERT INTO ""Fileset"" (""ID"", ""OperationID"", ""VolumeID"", ""IsFullBackup"", ""Timestamp"")
                VALUES (1, 1, 2, 1, 0)";
            cmd.ExecuteNonQuery();

            // Insert 3 orphaned FilesetEntry records - these reference FileIDs that do NOT exist in FileLookup
            // This simulates a database corruption scenario with multiple orphaned records
            cmd.CommandText = @"
                INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (1, 99999, 0);
                INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (1, 99998, 0);
                INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (1, 99997, 0);";
            cmd.ExecuteNonQuery();

            // Close the connection so LocalDatabase can open it
            db.Close();

            // Create LocalDatabase instance
            await using var localDb = await LocalDatabase.CreateLocalDatabaseAsync(
                dbfile,
                "TestOperation",
                true,
                null,
                CancellationToken.None
            );

            // Act & Assert: RemoveRemoteVolumes should throw ConstraintException
            ConstraintException? caughtException = null;
            try
            {
                await localDb.RemoveRemoteVolumes(new[] { "block-volume.zip" }, CancellationToken.None);
            }
            catch (ConstraintException ex)
            {
                caughtException = ex;
            }

            Assert.That(caughtException, Is.TypeOf<ConstraintException>());
            // Verify the exception message mentions the count of orphaned files
            Assert.That(caughtException!.Message, Does.Contain("3"));
            Assert.That(caughtException.Message, Does.Contain("file(s)"));
        }
    }
}