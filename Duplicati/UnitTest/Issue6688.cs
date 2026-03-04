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
using System.IO;
using System.Linq;
using NUnit.Framework;
using Duplicati.Library.Main;
using Microsoft.Data.Sqlite;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Test for Issue 6688: Recreate makes bad DB referencing Temporary Remotevolume ID if dindex lacks dblock
    /// 
    /// When recreating a database from remote files, if a dindex file references a dblock that is missing,
    /// the recreate operation may complete with apparent success but the resulting database contains
    /// invalid references - DeletedBlock entries pointing to Temporary RemoteVolume IDs.
    /// </summary>
    public class Issue6688 : BasicSetupHelper
    {
        [Test]
        [Category("Issue6688")]
        [Category("Recreate")]
        public void RecreateDbShouldNotReferenceTemporaryVolumesInDeletedBlock()
        {
            // Arrange
            var testopts = TestOptions;
            testopts["backup-test-samples"] = "0";
            testopts["no-encryption"] = "true";
            testopts["keep-versions"] = "0";
            testopts["blocksize"] = "1024"; // Small block size for testing

            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            Directory.CreateDirectory(sourceFolder);

            // Step 1: Create a file with enough content to create blocks and perform initial backup
            var testContent = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Line {i}: Test content for issue 6688 - this is test data to ensure we have enough content to create blocks."));
            File.WriteAllText(Path.Combine(sourceFolder, "test.txt"), testContent);
            
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, res.Errors.Count(), "Initial backup should succeed");
            }

            // Get the dblock files (there may be multiple with small blocksize)
            var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*");
            Assert.IsTrue(dblockFiles.Length > 0, "Should have at least one dblock file");
            var dblockPath = dblockFiles[0];
            
            // Get the dindex files
            var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*");
            Assert.IsTrue(dindexFiles.Length > 0, "Should have at least one dindex file");

            // Step 2: Delete the local database to force recreate
            File.Delete(testopts["dbpath"]);

            // Step 3: Delete the dblock file (simulate corruption/missing file)
            // Keep the dindex file which now references a missing dblock
            File.Delete(dblockPath);
            
            TestContext.Progress.WriteLine($"Deleted dblock: {Path.GetFileName(dblockPath)}");
            TestContext.Progress.WriteLine($"Remaining dindex files: {string.Join(", ", dindexFiles.Select(Path.GetFileName))}");

            // Step 4: Recreate the database
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                try
                {
                    var res = c.Repair();
                    TestContext.Progress.WriteLine($"Repair result: {res.ParsedResult}");
                    
                    // The operation may succeed with warnings, but we need to verify DB state
                }
                catch (Exception ex)
                {
                    // It's acceptable for repair to fail with a clear error
                    TestContext.Progress.WriteLine($"Repair failed: {ex.Message}");
                }
            }

            // Step 5: Verify the database does not contain invalid references
            VerifyNoInvalidDeletedBlockReferences(testopts["dbpath"]);
        }
        
        [Test]
        [Category("Issue6688")]
        [Category("Recreate")]
        public void RecreateDbShouldNotReferenceTemporaryVolumes_WhenDindexReferencesMissingDblock()
        {
            // This test more closely reproduces the issue scenario:
            // 1. Create two backups (versions)
            // 2. Delete the first version's dblock file
            // 3. Keep the first version's dindex file (which references the deleted dblock)
            // 4. Delete the database
            // 5. Recreate - this should handle the missing dblock gracefully
            
            var testopts = TestOptions;
            testopts["backup-test-samples"] = "0";
            testopts["no-encryption"] = "true";
            testopts["keep-versions"] = "0";

            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            Directory.CreateDirectory(sourceFolder);

            // Step 1: Create file A and backup (Version 1)
            File.WriteAllText(Path.Combine(sourceFolder, "A.txt"), "Content A - version 1");
            
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, res.Errors.Count(), "First backup should succeed");
            }

            // Save the dindex file from first backup
            var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*");
            Assert.IsTrue(dindexFiles.Length > 0, "Should have dindex after first backup");
            var dindex1Path = dindexFiles[0];
            var savedDindexPath = Path.Combine(DATAFOLDER, "dindex1_backup.zip");
            File.Copy(dindex1Path, savedDindexPath, true);
            
            // Get the first backup's dblock
            var dblockFiles1 = Directory.GetFiles(TARGETFOLDER, "*.dblock.*");
            Assert.IsTrue(dblockFiles1.Length > 0, "Should have dblock after first backup");
            var firstDblock = dblockFiles1[0];

            // Step 2: Modify file and backup again (Version 2)
            File.WriteAllText(Path.Combine(sourceFolder, "A.txt"), "Content A - version 2 - modified");
            
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, res.Errors.Count(), "Second backup should succeed");
            }

            // Step 3: Delete the database and prepare for recreate
            File.Delete(testopts["dbpath"]);
            
            // Delete the first dblock file (simulating missing dblock)
            File.Delete(firstDblock);
            
            // Copy the old dindex back (it references the now-deleted dblock)
            // Rename it to ensure it's processed early
            var earlyDindexPath = Path.Combine(TARGETFOLDER, "duplicati-00000000000000000000000000000000.dindex.zip");
            File.Copy(savedDindexPath, earlyDindexPath, true);
            
            TestContext.Progress.WriteLine($"Deleted first dblock: {Path.GetFileName(firstDblock)}");
            TestContext.Progress.WriteLine($"Restored old dindex: {Path.GetFileName(savedDindexPath)}");

            // Step 4: Recreate the database
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                try
                {
                    var res = c.Repair();
                    TestContext.Progress.WriteLine($"Repair result: {res.ParsedResult}");
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"Repair failed: {ex.Message}");
                }
            }

            // Step 5: Verify the database does not contain invalid references
            VerifyNoInvalidDeletedBlockReferences(testopts["dbpath"]);
        }
        
        private void VerifyNoInvalidDeletedBlockReferences(string dbPath)
        {
            using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=false");
            connection.Open();
            var command = connection.CreateCommand();

            // Check for DeletedBlock entries pointing to Temporary volumes
            command.CommandText = @"
                SELECT COUNT(*)
                FROM DeletedBlock
                WHERE VolumeID IN (SELECT ID FROM RemoteVolume WHERE State = 'Temporary')
            ";
            var tempCount = (long)command.ExecuteScalar();

            // Check for DeletedBlock entries with orphaned VolumeID references
            command.CommandText = @"
                SELECT COUNT(*)
                FROM DeletedBlock
                WHERE VolumeID NOT IN (SELECT ID FROM RemoteVolume)
            ";
            var orphanedCount = (long)command.ExecuteScalar();
            
            // Log diagnostic information
            TestContext.Progress.WriteLine($"DeletedBlock entries referencing Temporary volumes: {tempCount}");
            TestContext.Progress.WriteLine($"DeletedBlock entries with orphaned VolumeID: {orphanedCount}");
            
            // List all RemoteVolumes and their states
            command.CommandText = @"
                SELECT ID, Name, State, Type FROM RemoteVolume ORDER BY Type, Name
            ";
            using (var reader = command.ExecuteReader())
            {
                TestContext.Progress.WriteLine("\nRemoteVolume entries:");
                while (reader.Read())
                {
                    var id = reader.GetInt64(0);
                    var name = reader.GetString(1);
                    var state = reader.GetString(2);
                    var type = reader.GetString(3);
                    TestContext.Progress.WriteLine($"  ID={id}, Name={name}, State={state}, Type={type}");
                }
            }
            
            // List all DeletedBlock entries
            command.CommandText = @"
                SELECT db.ID, db.Hash, db.Size, db.VolumeID, rv.State
                FROM DeletedBlock db
                LEFT JOIN RemoteVolume rv ON db.VolumeID = rv.ID
            ";
            using (var reader = command.ExecuteReader())
            {
                TestContext.Progress.WriteLine("\nDeletedBlock entries:");
                while (reader.Read())
                {
                    var id = reader.GetInt64(0);
                    var hash = reader.GetString(1);
                    var size = reader.GetInt64(2);
                    var volId = reader.GetInt64(3);
                    var state = reader.IsDBNull(4) ? "NULL (orphaned)" : reader.GetString(4);
                    TestContext.Progress.WriteLine($"  ID={id}, Hash={hash[..Math.Min(16, hash.Length)]}..., Size={size}, VolumeID={volId}, VolumeState={state}");
                }
            }

            // These assertions will fail if the bug exists
            Assert.AreEqual(0, tempCount, 
                "DeletedBlock table should not contain entries referencing Temporary volumes. " +
                $"Found {tempCount} invalid entries referencing Temporary volumes.");

            Assert.AreEqual(0, orphanedCount,
                "DeletedBlock table should not contain entries with orphaned VolumeID references. " +
                $"Found {orphanedCount} orphaned entries.");
        }
    }
}
