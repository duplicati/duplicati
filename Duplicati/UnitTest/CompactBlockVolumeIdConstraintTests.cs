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

using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    public class CompactBlockVolumeIdConstraintTests : BasicSetupHelper
    {
        /// <summary>
        /// Attempt to reproduce a crash when compacting a database with a controller sequence
        /// </summary>
        [Test]
        [Category("Disruption"), Category("Bug")]
        public void CompactWithControllerSequence_ShouldNotCrash()
        {
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["backup-test-samples"] = "0",
                ["number-of-retries"] = "0",
                ["dblock-size"] = "10KB",
                ["blocksize"] = "1KB",
                ["synchronous-upload"] = "true"
            };

            string target = "file://" + TARGETFOLDER;

            string smallFile = Path.Combine(DATAFOLDER, "small.bin");
            string largeFile = Path.Combine(DATAFOLDER, "large.bin");

            byte[] smallContent = new byte[1024];
            new Random(42).NextBytes(smallContent);
            File.WriteAllBytes(smallFile, smallContent);

            byte[] largeContent = new byte[25 * 1024];
            Array.Copy(smallContent, largeContent, 1024);
            new Random(43).NextBytes(largeContent.AsSpan(1024, largeContent.Length - 1024));
            File.WriteAllBytes(largeFile, largeContent);

            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                c.Backup(new[] { DATAFOLDER });
            }

            File.Delete(largeFile);

            testopts["no-auto-compact"] = "true";
            testopts["keep-versions"] = "1";
            testopts["allow-full-removal"] = "true";

            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                c.Backup(new[] { DATAFOLDER });
            }

            testopts.Remove("no-auto-compact");
            testopts["threshold"] = "5";

            // Run compact, it should not crash.
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                c.Compact();
            }
        }

        /// <summary>
        /// This test follows a natural backup flow and then simulates a database
        /// inconsistency where blocks exist in both Block and DeletedBlock tables.
        /// 
        /// The normal backup/delete flow atomically moves blocks between these tables,
        /// so a purely natural sequence cannot create the overlapping state. However,
        /// this test replicates the state that would occur if cleanup failed, by
        /// using a targeted SQL statement after the controller sequence.
        /// </summary>
        [Test]
        [Category("Disruption"), Category("Bug")]
        public void CompactWithControllerSequence_AddBackFile_TriggersNotNullConstraint()
        {
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["backup-test-samples"] = "0",
                ["number-of-retries"] = "0",
                ["dblock-size"] = "10KB",
                ["blocksize"] = "1KB",
                ["synchronous-upload"] = "true",
                ["no-auto-compact"] = "true"
            };

            string target = "file://" + TARGETFOLDER;

            string smallFile = Path.Combine(DATAFOLDER, "small.bin");
            string largeFile = Path.Combine(DATAFOLDER, "large.bin");

            byte[] smallContent = new byte[1024];
            new Random(42).NextBytes(smallContent);
            File.WriteAllBytes(smallFile, smallContent);

            byte[] largeContent = new byte[25 * 1024];
            Array.Copy(smallContent, largeContent, 1024);
            new Random(43).NextBytes(largeContent.AsSpan(1024, largeContent.Length - 1024));
            File.WriteAllBytes(largeFile, largeContent);

            // Step 1: Backup with both files
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                c.Backup(new[] { DATAFOLDER });
            }

            // Step 2: Remove large file and backup (deletes old version, moves large blocks to DeletedBlock)
            File.Delete(largeFile);
            testopts["keep-versions"] = "1";
            testopts["allow-full-removal"] = "true";

            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                c.Backup(new[] { DATAFOLDER });
            }

            // Step 3: Add large file back and backup (moves large blocks back to Block)
            File.WriteAllBytes(largeFile, largeContent);
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                c.Backup(new[] { DATAFOLDER });
            }

            // Simulate the faulty database state that the natural flow does not create:
            // blocks exist in both Block and DeletedBlock with matching Hash/Size/VolumeID.
            // This replicates what would happen if a cleanup operation failed to remove
            // entries from DeletedBlock after moving them back to Block.
            using (var db = new SqliteConnection($"Data Source={DBFILE};Pooling=False"))
            {
                db.Open();
                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO ""DeletedBlock"" (""Hash"", ""Size"", ""VolumeID"")
                        SELECT ""Hash"", ""Size"", ""VolumeID"" FROM ""Block"";
                    ";
                    cmd.ExecuteNonQuery();
                }
            }

            testopts.Remove("no-auto-compact");
            testopts["threshold"] = "5";

            // Step 4: Compact - this triggers the bug in the unfixed codebase.
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                Exception? caughtException = null;
                try
                {
                    c.Compact();
                }
                catch (Exception ex)
                {
                    caughtException = ex;

                    if (caughtException is AggregateException aggEx && aggEx.InnerExceptions.Count > 0)
                        caughtException = aggEx.InnerExceptions.First();

                    if (caughtException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
                    {
                        Assert.Fail("The NOT NULL constraint error was triggered! The bug is still present: " + caughtException.Message);
                    }
                }

                if (caughtException == null)
                {
                    Assert.Fail("Expected an exception to be thrown due to the faulty DB state, but none was.");
                }
                else if (caughtException.Message.Contains("Unexpected number of rows updated"))
                {
                    Assert.Pass("The code safely handled the faulty database state and threw an unexpected rows exception instead of a NOT NULL constraint.");
                }
                else
                {
                    // For debugging other failures
                    Assert.Fail($"Threw unexpected exception: {caughtException}");
                }
            }
        }
    }
}
