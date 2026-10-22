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

#nullable enable

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.CommandLine.DatabaseTool;
using Duplicati.CommandLine.DatabaseTool.Commands;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class DatabaseToolCleanupScanTests
    {
        /// <summary>
        /// A minimal local database schema, enough for the tool to recognize it as a Duplicati database
        /// </summary>
        private const string LocalSchema = @"
            CREATE TABLE ""Version"" (""ID"" INTEGER PRIMARY KEY, ""Version"" INTEGER NOT NULL);
            INSERT INTO ""Version"" (""Version"") VALUES (12);
            CREATE TABLE ""Remotevolume"" (""ID"" INTEGER PRIMARY KEY, ""Name"" TEXT NOT NULL);
        ";

        /// <summary>
        /// A schema that has nothing to do with Duplicati
        /// </summary>
        private const string ForeignSchema = @"
            CREATE TABLE ""Photo"" (""ID"" INTEGER PRIMARY KEY, ""Path"" TEXT NOT NULL);
            INSERT INTO ""Photo"" (""Path"") VALUES ('holiday.jpg');
        ";

        /// <summary>
        /// Creates a SQLite database with the given schema
        /// </summary>
        /// <param name="path">The database file</param>
        /// <param name="schema">The SQL that creates the schema</param>
        private static async Task CreateDatabaseAsync(string path, string schema)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var db = await SQLiteLoader.LoadConnectionAsync(path);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = schema;
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// A SQLite file in the data folder that is not a Duplicati database is not
        /// reported as orphaned, so cleanup does not delete it
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task CleanupKeepsSqliteFilesThatAreNotDuplicatiDatabasesAsync()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            var tempDir = (string)tempFolder;

            var orphanDb = Path.Combine(tempDir, "ORPHANXYZA.sqlite");
            var foreignDb = Path.Combine(tempDir, "photos.sqlite");
            var nestedForeignDb = Path.Combine(tempDir, "other-app", "cache.sqlite");

            await CreateDatabaseAsync(orphanDb, LocalSchema);
            await CreateDatabaseAsync(foreignDb, ForeignSchema);
            await CreateDatabaseAsync(nestedForeignDb, ForeignSchema);

            var orphaned = await Verify.GetOrphanedDatabasesAsync(tempDir);
            Assert.That(orphaned.Select(x => x.Path), Is.EquivalentTo(new[] { Path.GetFullPath(orphanDb) }),
                "Only the Duplicati database should be reported as orphaned");

            Assert.That(await Program.MainAsync(["cleanup", "--datafolder", tempDir, "--force"]), Is.EqualTo(0));

            Assert.That(File.Exists(orphanDb), Is.False, "The orphaned Duplicati database should be deleted");
            Assert.That(File.Exists(foreignDb), Is.True, "A SQLite file of another application should be kept");
            Assert.That(File.Exists(nestedForeignDb), Is.True, "A SQLite file of another application in a subfolder should be kept");
        }
    }
}
