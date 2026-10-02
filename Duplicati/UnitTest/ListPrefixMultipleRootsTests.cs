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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The restore tree starts from the largest common prefix of the backed up paths. A backup
    /// spanning several drives, or a drive and a network share, has no common prefix, so it gets
    /// one root for each of them.
    /// </summary>
    [Category("Targeted")]
    public class ListPrefixMultipleRootsTests : BasicSetupHelper
    {
        /// <summary>
        /// Rewrites the paths in the local database so the backup looks as if it was made from
        /// the given Windows roots. Paths on the host only ever share a root, so this is the way
        /// to get several of them on any platform.
        /// </summary>
        /// <param name="roots">The local folder each source was backed up from, and the Windows path it gets</param>
        private async Task MoveSourcesToWindowsRootsAsync(IReadOnlyDictionary<string, string> roots)
        {
            await using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);

            var files = new List<(long ID, string Path)>();
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT ""A"".""ID"", ""B"".""Prefix"" || ""A"".""Path"" FROM ""FileLookup"" ""A"", ""PathPrefix"" ""B"" WHERE ""A"".""PrefixID"" = ""B"".""ID""";
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    files.Add((rd.GetInt64(0), rd.GetString(1)));
            }

            // The full path goes into the name under an empty prefix, which the File view joins back together
            long emptyPrefix;
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"INSERT OR IGNORE INTO ""PathPrefix"" (""Prefix"") VALUES ('')";
                await cmd.ExecuteNonQueryAsync();
                cmd.CommandText = @"SELECT ""ID"" FROM ""PathPrefix"" WHERE ""Prefix"" = ''";
                emptyPrefix = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            var moved = 0;
            foreach (var (id, path) in files)
            {
                var root = roots.Keys.FirstOrDefault(x => path.StartsWith(x, StringComparison.Ordinal));
                if (root == null)
                    continue;

                var newpath = roots[root] + path.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '\\');
                await using var cmd = db.CreateCommand();
                cmd.CommandText = @"UPDATE ""FileLookup"" SET ""PrefixID"" = @Prefix, ""Path"" = @Path WHERE ""ID"" = @ID";
                cmd.Parameters.AddWithValue("@Prefix", emptyPrefix);
                cmd.Parameters.AddWithValue("@Path", newpath);
                cmd.Parameters.AddWithValue("@ID", id);
                await cmd.ExecuteNonQueryAsync();
                moved++;
            }

            Assert.AreEqual(files.Count, moved, "Every backed up path should have been moved to a Windows root");
        }

        [Test]
        public async Task EachRootIsListedPromptlyAndNothingElse()
        {
            var sources = new[] { "srcA", "srcB", "srcC" }
                .Select(x => Path.Combine(DATAFOLDER, x) + Path.DirectorySeparatorChar)
                .ToArray();

            Directory.CreateDirectory(Path.Combine(sources[0], "sub"));
            File.WriteAllText(Path.Combine(sources[0], "a.txt"), "a");
            File.WriteAllText(Path.Combine(sources[0], "sub", "s.txt"), "s");
            Directory.CreateDirectory(sources[1]);
            File.WriteAllText(Path.Combine(sources[1], "b.txt"), "b");
            Directory.CreateDirectory(sources[2]);
            File.WriteAllText(Path.Combine(sources[2], "c.txt"), "c");

            var options = new Dictionary<string, string>(TestOptions);
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(sources));

            await MoveSourcesToWindowsRootsAsync(new Dictionary<string, string>
            {
                [sources[0]] = @"Q:\srcA\",
                [sources[1]] = @"R:\srcB\",
                [sources[2]] = @"\\server\share\srcC\",
            });

            options["list-prefix-only"] = "true";
            var sw = Stopwatch.StartNew();
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            {
                var result = await c.ListAsync();
                sw.Stop();

                CollectionAssert.AreEquivalent(
                    new[] { @"Q:\srcA\", @"R:\srcB\", @"\\server\share\srcC\" },
                    result.Files.Select(x => x.Path).ToArray(),
                    "Each drive and share should be one root, with no others");
            }

            // Each root used to wait for the database command timeout (30 seconds)
            Assert.Less(sw.Elapsed, TimeSpan.FromSeconds(15), "Listing the roots took {0}", sw.Elapsed);
        }
    }
}
