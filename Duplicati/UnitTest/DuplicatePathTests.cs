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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for duplicate path detection and cleanup in filesets.
    /// </summary>
    [TestFixture]
    public class DuplicatePathTests : BasicSetupHelper
    {
        /// <summary>
        /// Tests that RemoveDuplicatePathsFromFileset correctly removes duplicate entries.
        /// </summary>
        [Test]
        [Category("Targeted")]
        public async Task TestRemoveDuplicatePathsFromFileset()
        {
            // Create test files
            var testFile = Path.Combine(DATAFOLDER, "duplicate.txt");
            File.WriteAllText(testFile, "content");

            // Run initial backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var results = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, results.Errors.Count(), "Initial backup should succeed");
            }

            // Manually inject duplicate entries into the database
            // The database path is specified in TestOptions["dbpath"] = DBFILE
            var dbPath = DBFILE;
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();

                // Get the current fileset ID
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC LIMIT 1
                    ";
                    var filesetId = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                    Assert.Greater(filesetId, 0, "Should have a fileset");

                    // Get the FileID for our test file
                    // The "File" view already combines Prefix + Path
                    // We need to search for the path ending with our filename
                    // Use Path.Combine for cross-platform path handling
                    var searchPath = Path.Combine(DATAFOLDER, "duplicate.txt");
                    // Normalize path separators for the database query
                    cmd.CommandText = @"
                        SELECT ""ID"" FROM ""File""
                        WHERE ""Path"" = @Path
                        ORDER BY ""ID"" DESC LIMIT 1
                    ";
                    cmd.Parameters.AddWithValue("@Path", searchPath);
                    var fileId = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
                    Assert.Greater(fileId, 0, "Should have a file entry");

                    // Insert a duplicate entry with the same FileID
                    // This will fail due to the composite primary key constraint
                    // Instead, we need to create a different File entry with the same path
                    // First, get the existing FileLookup entry details
                    cmd.CommandText = @"
                        SELECT fl.""PrefixID"", fl.""Path"", fl.""BlocksetID"", fl.""MetadataID""
                        FROM ""FileLookup"" fl
                        WHERE fl.""ID"" = @FileId
                    ";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@FileId", fileId);
                    long prefixId = 0, blocksetId = 0, metadataId = 0;
                    string filePath = "";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            prefixId = reader.GetInt64(0);
                            filePath = reader.GetString(1);
                            blocksetId = reader.GetInt64(2);
                            metadataId = reader.GetInt64(3);
                        }
                    }

                    // Insert a new FileLookup entry with a different ID but same path
                    // Need a different blockset ID to avoid FileLookup unique constraint
                    cmd.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""ID"" != @BlocksetId LIMIT 1";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@BlocksetId", blocksetId);
                    var altBlocksetIdObj = await cmd.ExecuteScalarAsync();
                    long altBlocksetId;

                    if (altBlocksetIdObj == null)
                    {
                        // Need to create a new blockset
                        cmd.CommandText = @"
                            INSERT INTO ""Blockset"" (""Length"", ""FullHash"")
                            VALUES (0, 'abc123');
                            SELECT last_insert_rowid();
                        ";
                        altBlocksetId = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
                    }
                    else
                    {
                        altBlocksetId = Convert.ToInt64(altBlocksetIdObj);
                    }
                    Assert.Greater(altBlocksetId, 0, "Should have an alternative blockset");

                    // This simulates having two different versions of the same file
                    cmd.CommandText = @"
                        INSERT INTO ""FileLookup"" (""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"")
                        VALUES (@PrefixId, @Path, @BlocksetId, @MetadataId);
                        SELECT last_insert_rowid();
                    ";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@PrefixId", prefixId);
                    cmd.Parameters.AddWithValue("@Path", filePath);
                    cmd.Parameters.AddWithValue("@BlocksetId", altBlocksetId);
                    cmd.Parameters.AddWithValue("@MetadataId", metadataId);
                    var newFileId = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
                    Assert.Greater(newFileId, 0, "Should have created a new File entry");

                    // Now insert the duplicate FilesetEntry with the new FileID
                    cmd.CommandText = @"
                        INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"")
                        VALUES (@FilesetId, @FileId, @LastModified)
                    ";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@FilesetId", filesetId);
                    cmd.Parameters.AddWithValue("@FileId", newFileId);
                    cmd.Parameters.AddWithValue("@LastModified", DateTime.UtcNow.Ticks);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Now run the repair command which should fix the duplicates
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var results = c.Repair();
                Assert.AreEqual(0, results.Errors.Count(), "Repair should succeed");
            }

            // Verify no duplicates remain
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM (
                            SELECT f.""Path"", COUNT(*) as cnt
                            FROM ""FilesetEntry"" fe
                            JOIN ""File"" f ON fe.""FileID"" = f.""ID""
                            GROUP BY f.""Path""
                            HAVING cnt > 1
                        )
                    ";
                    var duplicateCount = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                    Assert.AreEqual(0, duplicateCount, "Database should have no duplicate paths after repair");
                }
            }
        }

        /// <summary>
        /// Tests that backup with --changed-files option does not create duplicates.
        /// </summary>
        [Test]
        [Category("Targeted")]
        public void TestChangedFilesDoesNotCreateDuplicates()
        {
            // Setup: Create test files
            var testFile1 = Path.Combine(DATAFOLDER, "file1.txt");
            var testFile2 = Path.Combine(DATAFOLDER, "file2.txt");

            File.WriteAllText(testFile1, "Initial content 1");
            File.WriteAllText(testFile2, "Initial content 2");

            // Step 1: Do initial backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
            }

            // Step 2: Modify one file
            File.WriteAllText(testFile1, "Modified content 1");

            // Step 3: Do backup with --changed-files
            var changedFilesOptions = new Dictionary<string, string>(TestOptions)
            {
                ["changed-files"] = testFile1
            };

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, changedFilesOptions, null))
            {
                var backupResults = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
            }

            // Step 4: Verify no duplicates in database
            var dbPath = DBFILE;
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                connection.Open();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM (
                            SELECT f.""Path"", COUNT(*) as cnt
                            FROM ""FilesetEntry"" fe
                            JOIN ""File"" f ON fe.""FileID"" = f.""ID""
                            GROUP BY fe.""FilesetID"", f.""Path""
                            HAVING cnt > 1
                        )
                    ";
                    var duplicateCount = (long)(cmd.ExecuteScalar() ?? 0);
                    Assert.AreEqual(0, duplicateCount, "Database should have no duplicate paths");
                }
            }
        }

        /// <summary>
        /// Tests that the repair command correctly handles multiple filesets with duplicates.
        /// </summary>
        [Test]
        [Category("Targeted")]
        public async Task TestRepairFixesDuplicatesAcrossMultipleFilesets()
        {
            // Create test files
            var testFile = Path.Combine(DATAFOLDER, "test.txt");
            File.WriteAllText(testFile, "content v1");

            // Run multiple backups
            for (int i = 0; i < 3; i++)
            {
                File.WriteAllText(testFile, $"content v{i + 1}");
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
                {
                    var results = c.Backup([DATAFOLDER]);
                    Assert.AreEqual(0, results.Errors.Count(), $"Backup {i + 1} should succeed");
                }
            }

            // Manually inject duplicates into all filesets
            var dbPath = DBFILE;
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();
                using (var cmd = connection.CreateCommand())
                {
                    // Get all fileset IDs
                    cmd.CommandText = @"SELECT ""ID"" FROM ""Fileset""";
                    var filesetIds = new List<long>();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            filesetIds.Add(reader.GetInt64(0));
                        }
                    }

                    // Get the FileID for our test file
                    // The "File" view already combines Prefix + Path
                    // Use Path.Combine for cross-platform path handling
                    var searchPath = Path.Combine(DATAFOLDER, "test.txt");
                    // Normalize path separators for the database query
                    // Need to replace both types of separators to handle cross-platform paths
                    searchPath = searchPath.Replace('\\', '/');
                    cmd.CommandText = @"
                        SELECT ""ID"" FROM ""File""
                        WHERE ""Path"" = @Path
                        ORDER BY ""ID"" DESC LIMIT 1
                    ";
                    cmd.Parameters.AddWithValue("@Path", searchPath);
                    var fileId = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);

                    // Get the existing FileLookup entry details to create a duplicate
                    cmd.CommandText = @"
                        SELECT fl.""PrefixID"", fl.""Path"", fl.""BlocksetID"", fl.""MetadataID""
                        FROM ""FileLookup"" fl
                        WHERE fl.""ID"" = @FileId
                    ";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@FileId", fileId);
                    long prefixId = 0, blocksetId = 0, metadataId = 0;
                    string filePath = "";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            prefixId = reader.GetInt64(0);
                            filePath = reader.GetString(1);
                            blocksetId = reader.GetInt64(2);
                            metadataId = reader.GetInt64(3);
                        }
                    }

                    // Get a different blockset ID to avoid unique constraint on FileLookup
                    // First try to find an existing blockset with different ID
                    cmd.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""ID"" != @BlocksetId LIMIT 1";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@BlocksetId", blocksetId);
                    var altBlocksetIdObj = await cmd.ExecuteScalarAsync();
                    long altBlocksetId;

                    if (altBlocksetIdObj == null)
                    {
                        // Need to create a new blockset - insert an empty blockset
                        cmd.CommandText = @"
                            INSERT INTO ""Blockset"" (""Length"", ""FullHash"")
                            VALUES (0, 'abc123');
                            SELECT last_insert_rowid();
                        ";
                        altBlocksetId = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
                    }
                    else
                    {
                        altBlocksetId = Convert.ToInt64(altBlocksetIdObj);
                    }
                    Assert.Greater(altBlocksetId, 0, "Should have an alternative blockset");

                    // Insert duplicate entries for each fileset with a new FileLookup entry
                    foreach (var filesetId in filesetIds)
                    {
                        // For each fileset, we need a unique FileLookup entry
                        // Create a new blockset for each duplicate to ensure uniqueness
                        cmd.CommandText = @"
                            INSERT INTO ""Blockset"" (""Length"", ""FullHash"")
                            VALUES (@Length, @Hash);
                            SELECT last_insert_rowid();
                        ";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@Length", filesetId); // Use filesetId to make it unique
                        cmd.Parameters.AddWithValue("@Hash", $"hash{filesetId}");
                        var uniqueBlocksetId = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);

                        // Create a new FileLookup entry with same path but unique blockset
                        cmd.CommandText = @"
                            INSERT INTO ""FileLookup"" (""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"")
                            VALUES (@PrefixId, @Path, @BlocksetId, @MetadataId);
                            SELECT last_insert_rowid();
                        ";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@PrefixId", prefixId);
                        cmd.Parameters.AddWithValue("@Path", filePath);
                        cmd.Parameters.AddWithValue("@BlocksetId", uniqueBlocksetId);
                        cmd.Parameters.AddWithValue("@MetadataId", metadataId);
                        var newFileId = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);

                        // Insert the duplicate FilesetEntry
                        cmd.CommandText = @"
                            INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"")
                            VALUES (@FilesetId, @FileId, @LastModified)
                        ";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@FilesetId", filesetId);
                        cmd.Parameters.AddWithValue("@FileId", newFileId);
                        cmd.Parameters.AddWithValue("@LastModified", DateTime.UtcNow.Ticks);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }

            // Count duplicates before repair
            int duplicatesBeforeRepair;
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM (
                            SELECT fe.""FilesetID"", f.""Path"", COUNT(*) as cnt
                            FROM ""FilesetEntry"" fe
                            JOIN ""File"" f ON fe.""FileID"" = f.""ID""
                            GROUP BY fe.""FilesetID"", f.""Path""
                            HAVING cnt > 1
                        )
                    ";
                    duplicatesBeforeRepair = (int)(long)(await cmd.ExecuteScalarAsync() ?? 0);
                }
            }

            Assert.Greater(duplicatesBeforeRepair, 0, "Should have duplicates before repair");

            // Clean up the fake blocksets we created to avoid consistency check failures
            // We need to delete the FilesetEntry rows that reference our fake blocksets first
            // Then delete the FileLookup entries, then the Blockset entries
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();
                using (var cmd = connection.CreateCommand())
                {
                    // Delete FilesetEntry rows for our fake FileLookup entries
                    // (those with blockset IDs that have no BlocksetEntry entries)
                    cmd.CommandText = @"
                        DELETE FROM ""FilesetEntry""
                        WHERE ""FileID"" IN (
                            SELECT fl.""ID"" FROM ""FileLookup"" fl
                            JOIN ""Blockset"" bs ON fl.""BlocksetID"" = bs.""ID""
                            LEFT JOIN ""BlocksetEntry"" bse ON bs.""ID"" = bse.""BlocksetID""
                            WHERE bse.""BlocksetID"" IS NULL AND bs.""Length"" > 0
                        )
                    ";
                    await cmd.ExecuteNonQueryAsync();

                    // Delete the fake FileLookup entries
                    cmd.CommandText = @"
                        DELETE FROM ""FileLookup""
                        WHERE ""ID"" IN (
                            SELECT fl.""ID"" FROM ""FileLookup"" fl
                            JOIN ""Blockset"" bs ON fl.""BlocksetID"" = bs.""ID""
                            LEFT JOIN ""BlocksetEntry"" bse ON bs.""ID"" = bse.""BlocksetID""
                            WHERE bse.""BlocksetID"" IS NULL AND bs.""Length"" > 0
                        )
                    ";
                    await cmd.ExecuteNonQueryAsync();

                    // Delete the fake Blockset entries
                    cmd.CommandText = @"
                        DELETE FROM ""Blockset""
                        WHERE ""ID"" NOT IN (SELECT DISTINCT ""BlocksetID"" FROM ""BlocksetEntry"")
                        AND ""Length"" > 0
                    ";
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Run repair
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var results = c.Repair();
                Assert.AreEqual(0, results.Errors.Count(), "Repair should succeed");
            }

            // Verify no duplicates after repair
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM (
                            SELECT fe.""FilesetID"", f.""Path"", COUNT(*) as cnt
                            FROM ""FilesetEntry"" fe
                            JOIN ""File"" f ON fe.""FileID"" = f.""ID""
                            GROUP BY fe.""FilesetID"", f.""Path""
                            HAVING cnt > 1
                        )
                    ";
                    var duplicateCount = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                    Assert.AreEqual(0, duplicateCount, "Database should have no duplicate paths after repair");
                }
            }
        }

        /// <summary>
        /// Tests that normal backup without duplicates works correctly.
        /// </summary>
        [Test]
        [Category("Targeted")]
        public void TestNormalBackupHasNoDuplicates()
        {
            // Create test files
            var testFile1 = Path.Combine(DATAFOLDER, "file1.txt");
            var testFile2 = Path.Combine(DATAFOLDER, "file2.txt");
            var testFile3 = Path.Combine(DATAFOLDER, "file3.txt");

            File.WriteAllText(testFile1, "content 1");
            File.WriteAllText(testFile2, "content 2");
            File.WriteAllText(testFile3, "content 3");

            // Run backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var results = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, results.Errors.Count());
            }

            // Verify no duplicates
            var dbPath = DBFILE;
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                connection.Open();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM (
                            SELECT fe.""FilesetID"", f.""Path"", COUNT(*) as cnt
                            FROM ""FilesetEntry"" fe
                            JOIN ""File"" f ON fe.""FileID"" = f.""ID""
                            GROUP BY fe.""FilesetID"", f.""Path""
                            HAVING cnt > 1
                        )
                    ";
                    var duplicateCount = (long)(cmd.ExecuteScalar() ?? 0);
                    Assert.AreEqual(0, duplicateCount, "Normal backup should not create duplicates");
                }
            }
        }
    }
}
