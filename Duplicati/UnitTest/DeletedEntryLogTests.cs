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
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// New and changed files are logged with their path at verbose level, so a report can list them.
    /// Deleted entries must be logged the same way, or a report cannot say which files went away (#2750).
    /// </summary>
    [Category("Targeted")]
    public class DeletedEntryLogTests : BasicSetupHelper
    {
        private sealed class LogSink : ILogDestination
        {
            public List<LogEntry> Entries { get; } = [];

            public void WriteMessage(LogEntry entry) => Entries.Add(entry);
        }

        [Test]
        public async Task BackupLogsPathsOfDeletedEntriesAsync()
        {
            var keep = Path.Combine(DATAFOLDER, "keep.txt");
            var removed = Path.Combine(DATAFOLDER, "removed.txt");
            var subfolder = Path.Combine(DATAFOLDER, "sub");
            var nested = Path.Combine(subfolder, "nested.txt");
            Directory.CreateDirectory(subfolder);
            File.WriteAllText(keep, "keep");
            File.WriteAllText(removed, "removed");
            File.WriteAllText(nested, "nested");

            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            File.Delete(removed);
            Directory.Delete(subfolder, true);

            var sink = new LogSink();
            IBackupResults res;
            using (Log.StartIsolatingScope(true))
            using (Log.StartScope(sink, LogMessageType.Verbose))
            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
                res = await c.BackupAsync([DATAFOLDER]);

            TestUtils.AssertResults(res);
            Assert.AreEqual(2, res.DeletedFiles);
            Assert.AreEqual(1, res.DeletedFolders);

            string[] Paths(string id) => sink.Entries
                .Where(x => x.Id == id)
                .Select(x => x.Arguments.Single()?.ToString() ?? "")
                .OrderBy(x => x)
                .ToArray();

            CollectionAssert.AreEqual(new[] { nested, removed }.OrderBy(x => x).ToArray(), Paths("DeletedFile"));
            CollectionAssert.AreEqual(new[] { subfolder + Path.DirectorySeparatorChar }, Paths("DeletedFolder"));

            // The filter tag is what --send-mail-log-filter and similar options match against
            Assert.IsTrue(sink.Entries.Where(x => x.Id == "DeletedFile").All(x => x.FilterTag.EndsWith("-DeletedFile")),
                string.Join(", ", sink.Entries.Where(x => x.Id == "DeletedFile").Select(x => x.FilterTag)));
        }
    }
}
