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
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The checks a repair runs on the local database report what they would fix in a dry run, and change nothing
    /// </summary>
    public class RepairDryRunDatabaseTests : BasicSetupHelper
    {
        private async Task<long> CountAsync(string table)
        {
            using var connection = await SQLiteLoader.LoadConnectionAsync(DBFILE).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = $@"SELECT COUNT(*) FROM ""{table}""";
            return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false));
        }

        private async Task ExecuteAsync(string sql)
        {
            using var connection = await SQLiteLoader.LoadConnectionAsync(DBFILE).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        [Test]
        [Category("Repair")]
        public async Task ADryRunRepairDoesNotRemoveADuplicatePath_Async()
        {
            var options = new Dictionary<string, string>(TestOptions) { ["no-encryption"] = "true" };
            File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "content");
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false));

            // A second entry with the path of the file, as a folder, in the same version: the path is twice in the version
            await ExecuteAsync(@"
                INSERT INTO ""FileLookup"" (""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"")
                SELECT ""PrefixID"", ""Path"", -100, ""MetadataID"" FROM ""FileLookup"" WHERE ""Path"" = 'file.txt';
                INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"")
                SELECT ""E"".""FilesetID"", (SELECT MAX(""ID"") FROM ""FileLookup""), ""E"".""Lastmodified""
                FROM ""FilesetEntry"" ""E"" JOIN ""FileLookup"" ""F"" ON ""F"".""ID"" = ""E"".""FileID"" WHERE ""F"".""Path"" = 'file.txt' AND ""F"".""BlocksetID"" != -100;
            ").ConfigureAwait(false);

            var fileLookup = await CountAsync("FileLookup").ConfigureAwait(false);
            var filesetEntries = await CountAsync("FilesetEntry").ConfigureAwait(false);

            using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(options) { ["dry-run"] = "true" }, null))
            {
                var results = await c.RepairAsync().ConfigureAwait(false);
                Assert.That(results.Errors, Is.Empty, "The dry run should report what it would repair, not fail");
            }

            Assert.That(await CountAsync("FileLookup").ConfigureAwait(false), Is.EqualTo(fileLookup), "A dry run should not remove file entries");
            Assert.That(await CountAsync("FilesetEntry").ConfigureAwait(false), Is.EqualTo(filesetEntries), "A dry run should not remove entries from a version");

            // Without the dry run, the repair removes the duplicates, so the dry run had something to leave alone
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                await c.RepairAsync().ConfigureAwait(false);

            Assert.That(await CountAsync("FilesetEntry").ConfigureAwait(false), Is.LessThan(filesetEntries), "The repair should remove the duplicate");
        }
    }
}
