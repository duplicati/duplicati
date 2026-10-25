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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The controller writes the result of every operation to the job log and runs auto-vacuum
    /// after it. A compact that changed something also did both itself, so its run showed up
    /// twice in the job log and vacuumed the database twice.
    /// </summary>
    [TestFixture]
    public class CompactResultLogTests : BasicSetupHelper
    {
        /// <summary>
        /// Makes two backups where the second leaves half the data unused, then compacts.
        /// </summary>
        private async Task BackupAndCompactAsync(bool autoVacuum)
        {
            var testopts = TestOptions.Expand(new
            {
                keep_versions = 1,
                no_auto_compact = true,
                zip_compression_level = 0,
                blocksize = "100kb",
                dblock_size = "1mb",
                no_encryption = true,
            });

            for (var i = 0; i < 20; i++)
            {
                var data = new byte[300 * 1024];
                Random.Shared.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"a{i}"), data);
            }

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            for (var i = 0; i < 10; i++)
                File.Delete(Path.Combine(DATAFOLDER, $"a{i}"));

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            testopts["auto-vacuum"] = autoVacuum ? "true" : "false";
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var result = await c.CompactAsync();
                TestUtils.AssertResults(result);
                Assert.That(result.DeletedFileCount, Is.GreaterThan(0), "The compact did not change anything");
            }
        }

        /// <summary>
        /// Runs a count query against the local database.
        /// </summary>
        private async Task<long> CountAsync(string sql)
        {
            await using var con = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            await using var cmd = con.CreateCommand();
            return await cmd.SetCommandAndParameters(sql).ExecuteScalarInt64Async(-1, CancellationToken.None);
        }

        /// <summary>
        /// One compact run is one entry in the job log.
        /// </summary>
        [Test]
        [Category("Serialization")]
        public async Task ACompactThatChangesSomethingIsLoggedOnceAsync()
        {
            await BackupAndCompactAsync(false);

            var entries = await CountAsync(@"SELECT COUNT(*) FROM ""LogData"" WHERE ""Type"" = 'Result' AND ""Message"" LIKE '%""MainOperation"":""Compact""%'");
            Assert.That(entries, Is.EqualTo(1), "The compact run is shown more than once in the job log");
        }

        /// <summary>
        /// Auto-vacuum runs once after a compact. Each vacuum records an operation of its own.
        /// </summary>
        [Test]
        [Category("Serialization")]
        public async Task AutoVacuumRunsOnceAfterACompactAsync()
        {
            await BackupAndCompactAsync(true);

            var vacuums = await CountAsync(@"SELECT COUNT(*) FROM ""Operation"" WHERE ""Description"" = 'Vacuum'");
            Assert.That(vacuums, Is.EqualTo(1), "Auto-vacuum did not run exactly once after the compact");
        }
    }
}
