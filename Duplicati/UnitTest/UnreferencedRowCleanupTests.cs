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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The cleanup that follows a fileset being dropped decides what to keep by asking
    /// which rows are still referenced. These tests pin that decision down: a row another
    /// fileset still uses has to survive, and a row nothing points at any more has to go.
    ///
    /// They exist because the queries behind it are rewritten from time to time for speed
    /// (see #7180 and #7247), and a rewrite that changes what the query means would
    /// otherwise be silent - it deletes backup history, and nothing complains.
    /// </summary>
    [TestFixture]
    public class UnreferencedRowCleanupTests
    {
        /// <summary>The timestamp of the fileset that the tests drop</summary>
        private const long DroppedFilesetTime = 1000;
        /// <summary>The timestamp of the fileset that the tests keep</summary>
        private const long KeptFilesetTime = 2000;

        /// <summary>
        /// Builds a database with two filesets. One file, its metadata and their blocks are
        /// shared by both; a second file is used only by the fileset that gets dropped.
        /// </summary>
        /// <returns>The database file, which the caller owns</returns>
        private static async Task<TempFile> CreateTwoFilesetDatabaseAsync()
        {
            var dbfile = new TempFile();
            try
            {
                using (var db = await SQLiteLoader.LoadConnectionAsync(dbfile))
                {
                    DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));
                    using var cmd = db.CreateCommand();

                    foreach (var sql in new[]
                    {
                        @"INSERT INTO ""Operation"" (""ID"", ""Description"", ""Timestamp"") VALUES (1, 'Test', 0)",

                        // One fileset volume per fileset, plus the volume the blocks live in
                        @"INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""Size"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                          VALUES (1, 1, 'dropped.dlist.zip', 'Files', 10, 'Verified', 0, 0, 0, 0)",
                        @"INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""Size"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                          VALUES (2, 1, 'kept.dlist.zip', 'Files', 20, 'Verified', 0, 0, 0, 0)",
                        @"INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""Size"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                          VALUES (3, 1, 'blocks.dblock.zip', 'Blocks', 30, 'Verified', 0, 0, 0, 0)",

                        $@"INSERT INTO ""Fileset"" (""ID"", ""OperationID"", ""VolumeID"", ""IsFullBackup"", ""Timestamp"") VALUES (1, 1, 1, 1, {DroppedFilesetTime})",
                        $@"INSERT INTO ""Fileset"" (""ID"", ""OperationID"", ""VolumeID"", ""IsFullBackup"", ""Timestamp"") VALUES (2, 1, 2, 1, {KeptFilesetTime})",

                        // Blocksets 10 and 30 belong to the shared file, 20 and 40 to the one
                        // that only the dropped fileset uses
                        @"INSERT INTO ""Blockset"" (""ID"", ""Length"", ""FullHash"") VALUES (10, 1, 'shared-content')",
                        @"INSERT INTO ""Blockset"" (""ID"", ""Length"", ""FullHash"") VALUES (20, 1, 'lonely-content')",
                        @"INSERT INTO ""Blockset"" (""ID"", ""Length"", ""FullHash"") VALUES (30, 1, 'shared-metadata')",
                        @"INSERT INTO ""Blockset"" (""ID"", ""Length"", ""FullHash"") VALUES (40, 1, 'lonely-metadata')",

                        @"INSERT INTO ""Block"" (""ID"", ""Hash"", ""Size"", ""VolumeID"") VALUES (10, 'shared-content', 1, 3)",
                        @"INSERT INTO ""Block"" (""ID"", ""Hash"", ""Size"", ""VolumeID"") VALUES (20, 'lonely-content', 1, 3)",
                        @"INSERT INTO ""Block"" (""ID"", ""Hash"", ""Size"", ""VolumeID"") VALUES (30, 'shared-metadata', 1, 3)",
                        @"INSERT INTO ""Block"" (""ID"", ""Hash"", ""Size"", ""VolumeID"") VALUES (40, 'lonely-metadata', 1, 3)",

                        @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") VALUES (10, 0, 10)",
                        @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") VALUES (20, 0, 20)",
                        @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") VALUES (30, 0, 30)",
                        @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") VALUES (40, 0, 40)",

                        @"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (10, 0, 'shared-blocklist')",
                        @"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (20, 0, 'lonely-blocklist')",

                        @"INSERT INTO ""Metadataset"" (""ID"", ""BlocksetID"") VALUES (100, 30)",
                        @"INSERT INTO ""Metadataset"" (""ID"", ""BlocksetID"") VALUES (200, 40)",

                        @"INSERT INTO ""PathPrefix"" (""ID"", ""Prefix"") VALUES (1, '/')",
                        @"INSERT INTO ""FileLookup"" (""ID"", ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"") VALUES (1000, 1, 'shared.txt', 10, 100)",
                        @"INSERT INTO ""FileLookup"" (""ID"", ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"") VALUES (2000, 1, 'lonely.txt', 20, 200)",

                        // The shared file is in both filesets, the lonely one only in the dropped fileset
                        @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (1, 1000, 0)",
                        @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (2, 1000, 0)",
                        @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (1, 2000, 0)",
                    })
                    {
                        cmd.CommandText = sql;
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await db.CloseAsync();
                }

                return dbfile;
            }
            catch
            {
                dbfile.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Drops the fileset at <see cref="DroppedFilesetTime"/> and commits.
        /// </summary>
        /// <param name="dbfile">The database to work on</param>
        private static async Task DropTheFirstFilesetAsync(string dbfile)
        {
            await using var db = await LocalDeleteDatabase.CreateAsync(dbfile, "Test", null, CancellationToken.None);

            var removed = new List<KeyValuePair<string, long>>();
            await foreach (var v in db.DropFilesetsFromTableAsync(
                [new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(DroppedFilesetTime)],
                CancellationToken.None))
                removed.Add(v);

            Assert.AreEqual(1, removed.Count, "exactly the volume of the dropped fileset is up for removal");
            Assert.AreEqual("dropped.dlist.zip", removed[0].Key);

            await db.CommitAsync(CancellationToken.None);
        }

        /// <summary>
        /// Counts the rows of a table whose ID is in the given set.
        /// </summary>
        /// <param name="dbfile">The database to read</param>
        /// <param name="table">The table to count in</param>
        /// <param name="column">The column to match on</param>
        /// <param name="ids">The values to look for</param>
        /// <returns>The number of matching rows</returns>
        private static async Task<long> CountAsync(string dbfile, string table, string column, params long[] ids)
        {
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = $@"SELECT COUNT(*) FROM ""{table}"" WHERE ""{column}"" IN ({string.Join(",", ids)})";
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        [Test]
        [Category("Database")]
        public async Task DroppingAFilesetKeepsRowsThatAnotherFilesetStillUses()
        {
            using var dbfile = await CreateTwoFilesetDatabaseAsync();
            await DropTheFirstFilesetAsync(dbfile);

            Assert.AreEqual(1, await CountAsync(dbfile, "FileLookup", "ID", 1000), "the file the kept fileset still lists");
            Assert.AreEqual(1, await CountAsync(dbfile, "Metadataset", "ID", 100), "its metadata");
            Assert.AreEqual(2, await CountAsync(dbfile, "Blockset", "ID", 10, 30), "the blocksets of both");
            Assert.AreEqual(2, await CountAsync(dbfile, "BlocksetEntry", "BlocksetID", 10, 30), "and their entries");
            Assert.AreEqual(1, await CountAsync(dbfile, "BlocklistHash", "BlocksetID", 10), "and its blocklist hash");
            Assert.AreEqual(1, await CountAsync(dbfile, "FilesetEntry", "FilesetID", 2), "the kept fileset keeps its entry");
        }

        [Test]
        [Category("Database")]
        public async Task DroppingTheLastFilesetToUseARowRemovesIt()
        {
            using var dbfile = await CreateTwoFilesetDatabaseAsync();
            await DropTheFirstFilesetAsync(dbfile);

            Assert.AreEqual(0, await CountAsync(dbfile, "FileLookup", "ID", 2000), "the file nothing lists any more");
            Assert.AreEqual(0, await CountAsync(dbfile, "Metadataset", "ID", 200), "its metadata");
            Assert.AreEqual(0, await CountAsync(dbfile, "Blockset", "ID", 20, 40), "the blocksets of both");
            Assert.AreEqual(0, await CountAsync(dbfile, "BlocksetEntry", "BlocksetID", 20, 40), "and their entries");
            Assert.AreEqual(0, await CountAsync(dbfile, "BlocklistHash", "BlocksetID", 20), "and its blocklist hash");
            Assert.AreEqual(0, await CountAsync(dbfile, "FilesetEntry", "FilesetID", 1), "the dropped fileset keeps nothing");
            Assert.AreEqual(0, await CountAsync(dbfile, "Fileset", "ID", 1), "and is gone itself");
        }

        [Test]
        [Category("Database")]
        public async Task AFileNoFilesetListsIsCountedAsAnOrphan()
        {
            using var dbfile = await CreateTwoFilesetDatabaseAsync();

            // Take the lonely file out of the only fileset that listed it, without
            // running the cleanup, which is the state the count is there to find
            using (var raw = await SQLiteLoader.LoadConnectionAsync(dbfile))
            {
                using var cmd = raw.CreateCommand();
                cmd.CommandText = @"DELETE FROM ""FilesetEntry"" WHERE ""FileID"" = 2000";
                await cmd.ExecuteNonQueryAsync();
                await raw.CloseAsync();
            }

            await using var db = await LocalPurgeDatabase.CreateAsync(dbfile, null, CancellationToken.None);
            Assert.AreEqual(1, await db.CountOrphanFilesAsync(CancellationToken.None));
        }

        [Test]
        [Category("Database")]
        public async Task AFileAFilesetStillListsIsNotAnOrphan()
        {
            using var dbfile = await CreateTwoFilesetDatabaseAsync();

            await using var db = await LocalPurgeDatabase.CreateAsync(dbfile, null, CancellationToken.None);
            Assert.AreEqual(0, await db.CountOrphanFilesAsync(CancellationToken.None),
                "every file here is listed by at least one fileset");
        }
    }
}
