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

using System.IO;
using System.Collections.Generic;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Interface;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for empty file handling in backup and database recreation scenarios.
/// </summary>
public class EmptyFileTests : BasicSetupHelper
{
    /// <summary>
    /// Tests that zero-byte files are correctly handled after database recreation.
    /// This reproduces the issue where empty files cause database inconsistency errors
    /// after recreating the database and running another backup.
    /// </summary>
    [Test]
    [Category("EmptyFile")]
    public async Task EmptyFileAfterDatabaseRecreate()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });

        // Create an empty file and a file with content
        var emptyFilePath = Path.Combine(DATAFOLDER, "empty-file.txt");
        var contentFilePath = Path.Combine(DATAFOLDER, "content-file.txt");

        File.WriteAllText(emptyFilePath, "");  // Empty file (0 bytes)
        File.WriteAllText(contentFilePath, "This is a file with some content.");  // Non-empty file

        // Run initial backup
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { DATAFOLDER }));

        // Verify the backup works
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Test(long.MaxValue));

        // Delete the local database to simulate database loss
        File.Delete(DBFILE);

        // Recreate the database using repair
        var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
        if (File.Exists(recreatedDatabaseFile))
            File.Delete(recreatedDatabaseFile);

        testopts["dbpath"] = recreatedDatabaseFile;

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Repair());

        // Add a new file to force the backup to process changes
        // This triggers the code path where the database inconsistency is detected
        File.WriteAllText(Path.Combine(DATAFOLDER, "new-file.txt"), "new content");

        // Verify database consistency after recreate
        var opts = new Options(testopts);
        await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(recreatedDatabaseFile, "test", true, null, CancellationToken.None))
        {
            try
            {
                await db.VerifyConsistency(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None);
            }
            catch (DatabaseInconsistencyException ex)
            {
                Assert.Fail($"Database inconsistency found after recreate: {ex.Message}");
            }
        }

        // Run another backup after database recreation
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { DATAFOLDER }));

        // Verify the backup is still consistent
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Test(long.MaxValue));
    }

    /// <summary>
    /// Tests that a backup with only an empty file works correctly through database recreation.
    /// </summary>
    [Test]
    [Category("EmptyFile")]
    public async Task OnlyEmptyFileAfterDatabaseRecreate()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });

        // Create only an empty file
        var emptyFilePath = Path.Combine(DATAFOLDER, "empty-file.txt");
        File.WriteAllText(emptyFilePath, "");

        // Run initial backup
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { DATAFOLDER }));

        // Delete the local database
        File.Delete(DBFILE);

        // Recreate the database using repair
        var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
        if (File.Exists(recreatedDatabaseFile))
            File.Delete(recreatedDatabaseFile);

        testopts["dbpath"] = recreatedDatabaseFile;

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Repair());

        // Add a new file to force the backup to process changes
        // This triggers the code path where the database inconsistency is detected
        File.WriteAllText(Path.Combine(DATAFOLDER, "new-file.txt"), "new content");

        // Run another backup after database recreation
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { DATAFOLDER }));

        // Verify the backup is consistent
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Test(long.MaxValue));
    }

    /// <summary>
    /// Tests that empty files with same hash algorithm for block and file work correctly.
    /// This tests the code path where fe.Blockhash is null but BlockHashAlgorithm == FileHashAlgorithm.
    /// </summary>
    [Test]
    [Category("EmptyFile")]
    public async Task EmptyFileWithSameHashAlgorithm()
    {
        // Use same hash algorithm for block and file - this triggers different code path
        var testopts = TestOptions.Expand(new
        {
            no_encryption = true,
            block_hash_algorithm = "SHA256",
            file_hash_algorithm = "SHA256"
        });

        // Create multiple empty files to increase chance of any collision issues
        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(Path.Combine(DATAFOLDER, $"empty-file-{i}.txt"), "");
        }

        // Also add a file with content
        File.WriteAllText(Path.Combine(DATAFOLDER, "content-file.txt"), "Some content here.");

        // Run initial backup
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { DATAFOLDER }));

        // Verify the backup works
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Test(long.MaxValue));

        // Delete the local database
        File.Delete(DBFILE);

        // Recreate the database using repair
        var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
        if (File.Exists(recreatedDatabaseFile))
            File.Delete(recreatedDatabaseFile);

        testopts["dbpath"] = recreatedDatabaseFile;

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Repair());

        // Add a new file to force the backup to process changes
        // This triggers the code path where the database inconsistency is detected
        File.WriteAllText(Path.Combine(DATAFOLDER, "new-file.txt"), "new content");

        // Verify database consistency immediately after recreate
        var opts = new Options(testopts);
        await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(recreatedDatabaseFile, "test", true, null, CancellationToken.None))
        {
            try
            {
                await db.VerifyConsistency(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None);
            }
            catch (DatabaseInconsistencyException ex)
            {
                Assert.Fail($"Database inconsistency found after recreate: {ex.Message}");
            }
        }

        // Run another backup after database recreation
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { DATAFOLDER }));

        // Verify the backup is still consistent
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Test(long.MaxValue));
    }

    /// <summary>
    /// This test reproduces the issue reported in Github issue #6822
    /// </summary>
    [Test]
    [Category("EmptyFile")]
    public void EmptyFileReportedScenario()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });

        // Create an empty file and a file with content (as described in bug report)
        File.WriteAllText(Path.Combine(DATAFOLDER, "empty-test.txt"), "");
        File.WriteAllText(Path.Combine(DATAFOLDER, "other-file.txt"), "Some content here.");

        // Run initial backup
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { DATAFOLDER }));

        // Delete the local database (simulating database loss)
        File.Delete(DBFILE);

        // Recreate the database using repair
        var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
        if (File.Exists(recreatedDatabaseFile))
            File.Delete(recreatedDatabaseFile);

        testopts["dbpath"] = recreatedDatabaseFile;

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Repair());

        // Add a new file to force the backup to process changes
        // This triggers the code path where the database inconsistency is detected
        File.WriteAllText(Path.Combine(DATAFOLDER, "new-file.txt"), "new content");

        // Run another backup after database recreation
        // If the bug is present, this will throw DatabaseInconsistencyException
        // with message: "Found inconsistency in the following files while validating database"
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { DATAFOLDER }));
    }
}
