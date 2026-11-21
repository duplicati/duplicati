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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The legacy restore works from a temporary list of every block to restore. The new restore reads the
    /// blocks of each file from the database as it restores the file, so it does not build that list, which for
    /// a restore of many files is a large part of the memory the restore uses (#7538).
    /// </summary>
    [Category("Targeted")]
    public class RestoreBlockListTests : BasicSetupHelper
    {
        private sealed class LogSink : ILogDestination
        {
            public List<LogEntry> Entries { get; } = [];

            public void WriteMessage(LogEntry entry) => Entries.Add(entry);
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public async Task OnlyTheLegacyRestoreBuildsTheBlockListAsync(bool legacy)
        {
            Directory.CreateDirectory(Path.Combine(DATAFOLDER, "sub"));
            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "first file");
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "sub", "b.bin"), Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray());

            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var options = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER,
                ["restore-legacy"] = legacy ? "true" : "false",
            };

            var sink = new LogSink();
            IRestoreResults res;
            using (Log.StartIsolatingScope(true))
            using (Log.StartScope(sink, LogMessageType.Verbose))
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                res = await c.RestoreAsync(["*"]);

            TestUtils.AssertResults(res);
            Assert.AreEqual(2, res.RestoredFiles);
            TestUtils.AssertDirectoryTreesAreEquivalent(DATAFOLDER, RESTOREFOLDER, true, "restored");

            // Written when the list of every block to restore has been built
            var built = sink.Entries.Any(x => x.Id == "RestoreSourceSize");
            Assert.AreEqual(legacy, built, legacy ? "The legacy restore did not build the block list" : "The new restore built the block list it does not use");
        }
    }
}
