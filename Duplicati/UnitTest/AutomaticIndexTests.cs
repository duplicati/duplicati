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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// When a query joins on something there is no index for, SQLite builds a transient index
    /// of its own and says so through its log. Each message is therefore a join that is not
    /// covered by an index. Reported as issue #5976.
    /// </summary>
    [TestFixture]
    // The SQLite log hook is process wide, so another test running at the same time would
    // have its messages counted here as well.
    [NonParallelizable]
    [Category("AutomaticIndex")]
    public class AutomaticIndexTests : BasicSetupHelper
    {
        /// <summary>
        /// The temporary tables are named with a fresh GUID on every run, so the names have to
        /// be reduced to the part that identifies the table before they can be compared.
        /// </summary>
        private static readonly Regex TempTableSuffix = new Regex("-[0-9A-Fa-f]{32}", RegexOptions.Compiled);

        /// <summary>
        /// The automatic indexes that an index cannot remove, and why.
        /// </summary>
        private static readonly HashSet<string> Accepted = new HashSet<string>(StringComparer.Ordinal)
        {
            // Aliases of subqueries, not of tables: the subquery is materialized without any
            // index, so an index on the tables it reads does not remove these. Removing them
            // means rewriting the queries.
            //   LocalDatabase.VerifyConsistencyInnerAsync, the grouped blockset lengths
            "B(BlocksetID)",
            //   LocalDatabase.VerifyConsistencyInnerAsync, the grouped blocklist hash counts
            "G(BlocksetID)",
            //   LocalDeleteDatabase.GetWastedSpaceReportAsync, the grouped scan times
            "B(VolumeID)",

            // Temporary tables that are created, read by a single statement and dropped again.
            // An explicit index on those costs the same as the transient one it would replace,
            // so it is left to SQLite. All of these are in LocalTestDatabase.
            "Blocklist(Hash)",
            "BlocklistHashList(Hash)",
            "CmpTable(Hash)",
            "CmpTable(Name)",
            "CmpTable(Path)",
            "CmpTable(Size)"
        };

        [Test]
        public async Task AutomaticIndexesStayWithinTheKnownSet()
        {
            SQLitePCL.Batteries_V2.Init();

            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            var rc = SQLitePCL.raw.sqlite3_config_log((_, code, msg) =>
            {
                if (msg == null)
                    return;

                var m = Regex.Match(msg, @"automatic index on (?<t>.+)$", RegexOptions.IgnoreCase);
                if (!m.Success)
                    return;

                // The log only names the alias the query used, so the call site is the only
                // way to tell which query it was. The callback runs on the thread that
                // prepared the statement, so its stack still holds the Duplicati frames.
                var frames = new System.Diagnostics.StackTrace(true).GetFrames()
                    .Select(f => new { Method = f.GetMethod(), File = f.GetFileName(), Line = f.GetFileLineNumber() })
                    .Where(x => x.Method?.DeclaringType?.FullName?.StartsWith("Duplicati.Library.Main.", StringComparison.Ordinal) == true)
                    .Select(x => $"      {x.Method!.DeclaringType!.Name}.{x.Method.Name} ({Path.GetFileName(x.File)}:{x.Line})")
                    .Take(6);

                var key = TempTableSuffix.Replace(m.Groups["t"].Value, "");
                lock (found)
                    if (!found.ContainsKey(key))
                        found[key] = string.Join(Environment.NewLine, frames);
            }, null);

            // The hook can only be installed while SQLite is idle, so the test has to be able
            // to say that it never got to look rather than passing quietly.
            Assert.AreEqual(SQLitePCL.raw.SQLITE_OK, rc,
                "Could not install the SQLite log hook, so nothing was observed");

            try
            {
                var testopts = TestOptions.Expand(new { no_encryption = true });

                // Large enough to span several blocks, so blocksets and blocklists are
                // exercised rather than only single block files
                var rng = new Random(42);
                var data = new byte[1024 * 1024];
                for (var i = 0; i < 3; i++)
                {
                    rng.NextBytes(data);
                    File.WriteAllBytes(Path.Combine(DATAFOLDER, $"file-{i}.bin"), data);
                }

                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                    TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

                // A second version, so the queries that compare against an earlier fileset run
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, "file-1.bin"), data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, "file-3.bin"), data);

                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                    TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                    TestUtils.AssertResults(await c.TestAsync(100));

                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                    TestUtils.AssertResults(await c.CompactAsync());

                var restoreopts = testopts.Expand(new { restore_path = RESTOREFOLDER });
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, restoreopts, null))
                    TestUtils.AssertResults(await c.RestoreAsync(["*"]));
            }
            finally
            {
                SQLitePCL.raw.sqlite3_config_log((SQLitePCL.strdelegate_log?)null, null);
            }

            var unexpected = found.Where(x => !Accepted.Contains(x.Key)).OrderBy(x => x.Key).ToList();
            Assert.IsEmpty(unexpected,
                $"SQLite had to build {unexpected.Count} automatic index(es) that are not in the known set. "
                + $"Either add an index for the join, or record it in {nameof(Accepted)} with the reason it cannot be indexed:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, unexpected.Select(x => $"  {x.Key}{Environment.NewLine}{x.Value}")));
        }
    }
}
