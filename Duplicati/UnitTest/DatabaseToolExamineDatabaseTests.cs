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
using System.Threading.Tasks;
using Duplicati.CommandLine.DatabaseTool;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class DatabaseToolExamineDatabaseTests
    {
        /// <summary>
        /// The version table every database examined by the tool has
        /// </summary>
        private const string VersionTable = @"
            CREATE TABLE ""Version"" (""ID"" INTEGER PRIMARY KEY, ""Version"" INTEGER NOT NULL);
            INSERT INTO ""Version"" (""Version"") VALUES (1);
        ";

        /// <summary>
        /// A server database is recognized by having both a Backup and a Schedule table,
        /// and an index, view or trigger that happens to be called Schedule is not a table
        /// </summary>
        /// <param name="schema">The schema besides the version table</param>
        /// <param name="expected">The expected database type</param>
        [Test]
        [Category("DatabaseTool")]
        [TestCase(@"CREATE TABLE ""Backup"" (""ID"" INTEGER); CREATE TABLE ""Schedule"" (""ID"" INTEGER);", DatabaseType.Server)]
        [TestCase(@"CREATE TABLE ""Backup"" (""ID"" INTEGER); CREATE INDEX ""Schedule"" ON ""Backup"" (""ID"");", DatabaseType.Backup)]
        [TestCase(@"CREATE TABLE ""Backup"" (""ID"" INTEGER); CREATE VIEW ""Schedule"" AS SELECT ""ID"" FROM ""Backup"";", DatabaseType.Backup)]
        [TestCase(@"CREATE TABLE ""Backup"" (""ID"" INTEGER); CREATE TRIGGER ""Schedule"" AFTER INSERT ON ""Backup"" BEGIN SELECT 1; END;", DatabaseType.Backup)]
        [TestCase(@"CREATE TABLE ""Schedule"" (""ID"" INTEGER); CREATE TRIGGER ""Schedule"" AFTER INSERT ON ""Schedule"" BEGIN SELECT 1; END;", DatabaseType.Backup)]
        public async Task ExamineDatabaseCountsOnlyTablesAsServerMarkersAsync(string schema, DatabaseType expected)
        {
            using var tempFolder = new Library.Utility.TempFolder();
            var dbPath = Path.Combine((string)tempFolder, "ABCDEFGHIJ.sqlite");

            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = VersionTable + schema;
                await cmd.ExecuteNonQueryAsync();
            }

            var (_, type) = await Helper.ExamineDatabaseAsync(dbPath);
            Assert.That(type, Is.EqualTo(expected));
        }
    }
}
