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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The temporary list of the files to restore had an index on their paths. Only the legacy restore looks files
    /// up by their path, and the index is about as large as the paths, in a table that is kept in memory (#7538).
    /// </summary>
    [Category("Targeted")]
    public class RestoreFilePathIndexTests : BasicSetupHelper
    {
        private sealed class LogSink : ILogDestination
        {
            public List<LogEntry> Entries { get; } = [];

            public void WriteMessage(LogEntry entry) => Entries.Add(entry);
        }

        // The statement that indexes the paths of the restore file list, as it is logged when it runs
        private static readonly Regex PathIndex = new(@"CREATE INDEX (IF NOT EXISTS )?""Fileset-[0-9A-F]+_Path""", RegexOptions.IgnoreCase);

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public async Task OnlyTheLegacyRestoreIndexesThePathsAsync(bool legacy)
        {
            Directory.CreateDirectory(Path.Combine(DATAFOLDER, "sub"));
            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "first file");
            File.WriteAllText(Path.Combine(DATAFOLDER, "sub", "b.txt"), "second file");

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
            using (Log.StartScope(sink, LogMessageType.Profiling))
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                res = await c.RestoreAsync(["*"]);

            TestUtils.AssertResults(res);
            Assert.AreEqual(2, res.RestoredFiles);
            TestUtils.AssertDirectoryTreesAreEquivalent(DATAFOLDER, RESTOREFOLDER, true, "restored");

            var indexed = sink.Entries.Any(x => PathIndex.IsMatch(x.FormattedMessage ?? ""));
            Assert.AreEqual(legacy, indexed, legacy ? "The legacy restore did not index the paths" : "The new restore indexed the paths it does not look up");
        }
    }
}
