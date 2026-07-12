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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class Issue2287 : BasicSetupHelper
    {
        /// <summary>
        /// https://github.com/duplicati/duplicati/issues/2287
        /// The "find" command must locate a file in ANY backup version, not only the newest one.
        /// A bare filename is turned into a wildcard filter (e.g. "*/target.txt"), which previously
        /// searched only the newest version; the all-versions fallback only triggered when the newest
        /// version had zero matches, so a file present only in an older version was silently missed
        /// whenever the newest version still contained a same-named file. "find" now searches all
        /// versions by default, while "list" keeps its newest-only default.
        /// </summary>
        [Test]
        [Category("Targeted")]
        public async Task FindSearchesAllVersionsAsync()
        {
            var testopts = TestOptions.Expand(new { no_encryption = true });

            var subA = Path.Combine(DATAFOLDER, "sub_a");
            var subB = Path.Combine(DATAFOLDER, "sub_b");
            Directory.CreateDirectory(subA);
            Directory.CreateDirectory(subB);
            var fileA = Path.Combine(subA, "target.txt");
            var fileB = Path.Combine(subB, "target.txt");
            File.WriteAllText(fileA, "present in newest and old version");
            File.WriteAllText(fileB, "present only in the old version");

            // Version 1: both sub_a/target.txt and sub_b/target.txt exist.
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new[] { DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count());
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(3));

            // Version 0 (newest): sub_b/target.txt is removed, so it now exists only in the old version.
            File.Delete(fileB);
            Directory.Delete(subB);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new[] { DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count());
            }

            // "find target.txt" must surface the older-only sub_b/target.txt as well as the newest one.
            var findOutput = RunListCommand(Duplicati.CommandLine.Commands.Find, "target.txt", testopts);
            Assert.IsTrue(findOutput.Contains("sub_a"), "find should list the newest match:" + Environment.NewLine + findOutput);
            Assert.IsTrue(findOutput.Contains("sub_b"), "find should also list the older-only match:" + Environment.NewLine + findOutput);

            // "list target.txt" keeps the newest-only behaviour, so it must NOT include the older-only file.
            var listOutput = RunListCommand(Duplicati.CommandLine.Commands.List, "target.txt", testopts);
            Assert.IsTrue(listOutput.Contains("sub_a"), "list should list the newest match:" + Environment.NewLine + listOutput);
            Assert.IsFalse(listOutput.Contains("sub_b"), "list should not list the older-only match:" + Environment.NewLine + listOutput);
        }

        private string RunListCommand(
            Func<TextWriter, Action<Library.Main.Controller>, List<string>, Dictionary<string, string>, Library.Utility.IFilter, int> command,
            string filename,
            Dictionary<string, string> baseopts)
        {
            using var sw = new StringWriter();
            // The list/find commands mutate the args and options, so hand each invocation its own copies.
            var args = new List<string> { "file://" + TARGETFOLDER, filename };
            var opts = new Dictionary<string, string>(baseopts);
            command(sw, _ => { }, args, opts, null);
            return sw.ToString();
        }
    }
}
