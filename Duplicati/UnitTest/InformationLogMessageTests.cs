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
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Information messages have no exception argument, so a <c>null</c> in its place becomes the
    /// message and the text becomes a format argument, which the log shows as
    /// "Error while formating".
    /// </summary>
    [Category("Targeted")]
    public class InformationLogMessageTests : BasicSetupHelper
    {
        private sealed class LogSink : ILogDestination
        {
            public List<LogEntry> Entries { get; } = [];

            public void WriteMessage(LogEntry entry) => Entries.Add(entry);
        }

        [Test]
        public async Task RestoreWithNothingToDoLogsItsMessageAsync()
        {
            File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "contents");

            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            // Restoring to where the files already are, unchanged, has nothing to write
            var sink = new LogSink();
            using (Log.StartIsolatingScope(true))
            using (Log.StartScope(sink, LogMessageType.Information))
            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(null));

            var entry = sink.Entries.SingleOrDefault(x => x.Id == "NoFilesNeededRestore");
            Assert.IsNotNull(entry, "The restore should have found nothing to restore");
            Assert.AreEqual("Restore completed but all files were already present", entry.FormattedMessage);
        }
    }
}
