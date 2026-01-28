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

using System.IO;
using NUnit.Framework;
using System.Linq;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Main;
using Microsoft.Data.Sqlite;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Test for Issue #6539: Remote delete failure sets bad DB state
    /// https://github.com/duplicati/duplicati/issues/6539
    /// </summary>
    public class Issue6539 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void DeleteFailureShouldNotMarkAsDeleted()
        {
            var testopts = TestOptions.Expand(new
            {
                no_encryption = true,
                number_of_retries = 0,  // No retries to make test deterministic
                blocksize = "10kb",
                dblock_size = "50kb"
            });

            // Create test data
            var testFile = Path.Combine(DATAFOLDER, "testfile.txt");
            File.WriteAllText(testFile, "Test data for issue 6539");

            // Run initial backup
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Run second backup to create a version we can delete
            File.WriteAllText(testFile, "Modified test data for issue 6539");
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Get the dlist file that will be deleted (Version 0 is the newest)
            var dlistFiles = Directory.GetFiles(TARGETFOLDER, "*.dlist.*");
            Assert.That(dlistFiles.Length, Is.GreaterThanOrEqualTo(2), "Should have at least 2 dlist files");
            var fileToDelete = Path.GetFileName(dlistFiles.OrderBy(f => f).Last());

            // Setup error backend to fail on delete
            BackendLoader.AddBackend(new DeterministicErrorBackend());
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                // Fail delete operations for the specific file
                if (action.IsDeleteOperation && remotename == fileToDelete)
                    return true;
                return false;
            };

            // Attempt to delete version 0 - this should fail
            var deleteOpts = testopts.Expand(new
            {
                version = "0",
                allow_full_removal = true
            });

            try
            {
                using (var c = new Controller(
                    new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER,
                    deleteOpts,
                    null))
                {
                    // This should throw because delete fails
                    c.Delete();
                }
            }
            catch
            {
                // Expected - delete operation failed
            }

            // Reset error generator
            DeterministicErrorBackend.ErrorGenerator = null;

            // Check database state - file should still be in Deleting state, not Deleted
            using (var connection = new SqliteConnection($"Data Source={DBFILE};Pooling=false"))
            {
                connection.Open();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT Name, State
                        FROM RemoteVolume
                        WHERE Name = @name";
                    cmd.Parameters.AddWithValue("@name", fileToDelete);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var state = reader.GetString(1);
                            // State should be "Deleting", not "Deleted"
                            Assert.That(state, Is.EqualTo("Deleting"),
                                $"File {fileToDelete} should be in 'Deleting' state, not '{state}'. " +
                                "When delete fails, the file should remain in Deleting state so it can be retried.");
                        }
                        else
                        {
                            Assert.Fail($"File {fileToDelete} not found in RemoteVolume table");
                        }
                    }
                }
            }

            // Verify the file still exists on remote
            Assert.That(File.Exists(Path.Combine(TARGETFOLDER, fileToDelete)), Is.True,
                "File should still exist on remote storage after failed delete");

            // Retry delete - this should succeed and clean up the state
            using (var c = new Controller("file://" + TARGETFOLDER, deleteOpts, null))
                c.Delete();

            // Check database state - file should now be in Deleted state
            using (var connection = new SqliteConnection($"Data Source={DBFILE};Pooling=false"))
            {
                connection.Open();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT Name, State
                        FROM RemoteVolume
                        WHERE Name = @name";
                    cmd.Parameters.AddWithValue("@name", fileToDelete);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var state = reader.GetString(1);
                            Assert.That(state, Is.EqualTo("Deleted"),
                                $"File {fileToDelete} should be in 'Deleted' state after retry delete");
                        }
                    }
                }
            }

            // Now run a test/verify operation - it should NOT report "not recorded in local storage"
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var result = c.Test(1);
                // Should not have errors about files not recorded in local storage
                Assert.That(result.Errors, Is.Empty,
                    "Test should not report errors about files not recorded in local storage");
            }
        }

        [Test]
        [Category("Targeted")]
        public void DeleteSuccessShouldMarkAsDeleted()
        {
            var testopts = TestOptions.Expand(new
            {
                no_encryption = true,
                number_of_retries = 0,
                blocksize = "10kb",
                dblock_size = "50kb"
            });

            // Create test data
            var testFile = Path.Combine(DATAFOLDER, "testfile.txt");
            File.WriteAllText(testFile, "Test data for issue 6539");

            // Run initial backup
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Run second backup
            File.WriteAllText(testFile, "Modified test data for issue 6539");
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Get the dlist file that will be deleted (Version 0 is the newest)
            var dlistFiles = Directory.GetFiles(TARGETFOLDER, "*.dlist.*");
            Assert.That(dlistFiles.Length, Is.GreaterThanOrEqualTo(2), "Should have at least 2 dlist files");
            var fileToDelete = Path.GetFileName(dlistFiles.OrderBy(f => f).Last());

            // Delete version 0 - this should succeed
            var deleteOpts = testopts.Expand(new
            {
                version = "0",
                allow_full_removal = true
            });

            using (var c = new Controller("file://" + TARGETFOLDER, deleteOpts, null))
                c.Delete();

            // Check database state - file should be marked as Deleted
            using (var connection = new SqliteConnection($"Data Source={DBFILE};Pooling=false"))
            {
                connection.Open();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT Name, State
                        FROM RemoteVolume
                        WHERE Name = @name";
                    cmd.Parameters.AddWithValue("@name", fileToDelete);

                    using (var reader = cmd.ExecuteReader())
                    {
                        // File might be completely removed from DB after successful delete
                        // or marked as Deleted - both are acceptable
                        if (reader.Read())
                        {
                            var state = reader.GetString(1);
                            Assert.That(state, Is.EqualTo("Deleted"),
                                $"File {fileToDelete} should be in 'Deleted' state after successful delete");
                        }
                        // If not found, that's also OK - file was fully cleaned up
                    }
                }
            }

            // Verify the file no longer exists on remote
            Assert.That(File.Exists(Path.Combine(TARGETFOLDER, fileToDelete)), Is.False,
                "File should not exist on remote storage after successful delete");
        }
    }
}
