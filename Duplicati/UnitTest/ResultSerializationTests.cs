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

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The result written to the operation log is serialized by JsonFormatSerializer, and the
    /// backend statistics nested inside it inherit the operation-level timestamps from the result
    /// base class without ever setting them. Reported as issue #3853.
    /// </summary>
    [TestFixture]
    public class ResultSerializationTests : BasicSetupHelper
    {
        /// <summary>
        /// The value a DateTime that was never assigned serializes to.
        /// </summary>
        private const string UnsetTimestamp = "0001-01-01T00:00:00";

        /// <summary>
        /// Runs a backup and returns what was written to the operation log as the result.
        /// </summary>
        private async Task<List<string>> BackupAndReadResultLogAsync()
        {
            var testopts = TestOptions.Expand(new { no_encryption = true });

            Directory.CreateDirectory(Path.Combine(DATAFOLDER, "folder"));
            File.WriteAllText(Path.Combine(DATAFOLDER, "folder", "file.txt"), "some data");

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var results = new List<string>();
            await using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
            await using (var cmd = con.CreateCommand())
            {
                cmd.SetCommandAndParameters(@"SELECT ""Message"" FROM ""LogData"" WHERE ""Type"" = 'Result'");
                await using (var rd = await cmd.ExecuteReaderAsync())
                    while (await rd.ReadAsync())
                        results.Add(rd.ConvertValueToString(0) ?? "");
            }

            Assert.That(results, Is.Not.Empty, "The backup wrote no result to the operation log");
            return results;
        }

        /// <summary>
        /// The timestamps on the backend statistics are never assigned, so serializing them puts a
        /// zero DateTime in the log where a reader expects a time.
        /// </summary>
        [Test]
        [Category("Serialization")]
        public async Task TheResultLogHasNoUnsetTimestampsAsync()
        {
            foreach (var result in await BackupAndReadResultLogAsync())
                Assert.That(result, Does.Not.Contain(UnsetTimestamp),
                    $"The result written to the operation log contains an unassigned timestamp:{System.Environment.NewLine}{result}");
        }

        /// <summary>
        /// Dropping the whole BackendStatistics object would also remove the unset timestamps, and
        /// would take the numbers worth reading with them. This fails if that is how it is fixed.
        /// </summary>
        [Test]
        [Category("Serialization")]
        public async Task TheResultLogStillReportsWhatTheBackendDidAsync()
        {
            foreach (var result in await BackupAndReadResultLogAsync())
                Assert.That(result, Does.Contain("BytesUploaded"),
                    $"The result written to the operation log no longer says what the backend did:{System.Environment.NewLine}{result}");
        }
    }
}
