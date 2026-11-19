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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Without index files, a recreate reads the block volumes one by one and builds the blocks and
    /// blockset entries from the blocklists it finds in each of them (#2889).
    /// The database it builds must hold the same blocks and blockset entries as the one the backup made.
    /// </summary>
    public class RecreateWithoutIndexFilesTests : BasicSetupHelper
    {
        /// <summary>
        /// The tables that describe the backup, by content rather than by row ids, which a recreate may number differently.
        /// </summary>
        private static readonly Dictionary<string, string> Contents = new()
        {
            ["Block"] = @"SELECT ""B"".""Hash"", ""B"".""Size"", ""V"".""Name"" FROM ""Block"" ""B"" LEFT JOIN ""RemoteVolume"" ""V"" ON ""V"".""ID"" = ""B"".""VolumeID""",
            ["Blockset"] = @"SELECT ""FullHash"", ""Length"" FROM ""Blockset""",
            ["BlocksetEntry"] = @"SELECT ""S"".""FullHash"", ""S"".""Length"", ""E"".""Index"", ""B"".""Hash"", ""B"".""Size"" FROM ""BlocksetEntry"" ""E"" JOIN ""Blockset"" ""S"" ON ""S"".""ID"" = ""E"".""BlocksetID"" JOIN ""Block"" ""B"" ON ""B"".""ID"" = ""E"".""BlockID""",
            ["BlocklistHash"] = @"SELECT ""S"".""FullHash"", ""S"".""Length"", ""L"".""Index"", ""L"".""Hash"" FROM ""BlocklistHash"" ""L"" JOIN ""Blockset"" ""S"" ON ""S"".""ID"" = ""L"".""BlocksetID""",
            ["FilesetEntry"] = @"SELECT ""F"".""Path"", ""S"".""FullHash"", ""T"".""Timestamp"" FROM ""FilesetEntry"" ""E"" JOIN ""FileLookup"" ""F"" ON ""F"".""ID"" = ""E"".""FileID"" LEFT JOIN ""Blockset"" ""S"" ON ""S"".""ID"" = ""F"".""BlocksetID"" JOIN ""Fileset"" ""T"" ON ""T"".""ID"" = ""E"".""FilesetID""",
        };

        private static async Task<Dictionary<string, string[]>> ReadContentsAsync(string dbpath)
        {
            var result = new Dictionary<string, string[]>();
            using var connection = await SQLiteLoader.LoadConnectionAsync(dbpath).ConfigureAwait(false);
            foreach (var (table, query) in Contents)
            {
                var rows = new List<string>();
                using var command = connection.CreateCommand();
                command.CommandText = query;
                using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                    rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "null" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
                rows.Sort(StringComparer.Ordinal);
                result[table] = rows.ToArray();
            }
            return result;
        }

        [Test]
        [Category("Recreate")]
        public async Task ARecreateFromTheBlockVolumesBuildsTheSameBlocksetsAsTheBackup_Async()
        {
            // 1 KB blocks give 32 hashes per blocklist, so a file of a few hundred KB has many blocklists,
            // and 1 MB volumes spread the blocks and blocklists of a file over several volumes.
            var options = new Dictionary<string, string>(TestOptions)
            {
                ["blocksize"] = "1KB",
                ["dblock-size"] = "1MB",
                ["no-encryption"] = "true",
            };

            var rng = new Random(2889);
            byte[] RandomBytes(int size)
            {
                var data = new byte[size];
                rng.NextBytes(data);
                return data;
            }

            for (var i = 0; i < 12; i++)
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"large{i}"), RandomBytes(200 * 1024 + i * 1000));

            // The first blocklist of "shared-b" is the first blocklist of "shared-a", in two blocksets
            var shared = RandomBytes(32 * 1024);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "shared-a"), shared.Concat(RandomBytes(5 * 1024)).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "shared-b"), shared.Concat(RandomBytes(40 * 1024)).ToArray());

            // Files of a single block have no blocklist
            for (var i = 0; i < 20; i++)
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"small{i}"), RandomBytes(10 + i * 50));

            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            {
                var results = await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false);
                Assert.That(results.Errors, Is.Empty);
                Assert.That(results.Warnings, Is.Empty);
            }

            // A second version changes the middle of a large file and adds another one
            using (var s = File.OpenWrite(Path.Combine(DATAFOLDER, "large3")))
            {
                s.Position = 100 * 1024;
                s.Write(RandomBytes(3000));
            }
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "large-new"), RandomBytes(150 * 1024));

            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            {
                var results = await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false);
                Assert.That(results.Errors, Is.Empty);
                Assert.That(results.Warnings, Is.Empty);
            }

            var blockVolumes = Directory.GetFiles(TARGETFOLDER, "*.dblock.*").Length;
            Assert.That(blockVolumes, Is.GreaterThan(3), "The blocks should be spread over several volumes");

            var expected = await ReadContentsAsync(DBFILE).ConfigureAwait(false);
            Assert.That(expected["BlocklistHash"], Is.Not.Empty);

            foreach (var f in Directory.GetFiles(TARGETFOLDER, "*.dindex.*"))
                File.Delete(f);
            File.Delete(DBFILE);

            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            {
                var results = await c.RepairAsync().ConfigureAwait(false);
                Assert.That(results.Errors, Is.Empty);
                Assert.That(results.Warnings, Is.Empty);
            }

            var recreated = await ReadContentsAsync(DBFILE).ConfigureAwait(false);
            foreach (var table in Contents.Keys)
                Assert.That(recreated[table], Is.EqualTo(expected[table]), $"The {table} table should be the same after the recreate");
        }
    }
}
