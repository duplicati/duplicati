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
using System.Linq;
using Duplicati.Library.Logging;
using Duplicati.Server;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The live log is polled the way the web UI polls <c>/api/v1/logdata/poll</c>: from the
    /// highest ID it has seen, a page at a time. More new lines than a page between two polls
    /// used to lose all but the newest page (#5572).
    /// </summary>
    [Category("Targeted")]
    public class LiveLogPollingTests
    {
        /// <summary>
        /// The page size the web UI asks for.
        /// </summary>
        private const int PageSize = 100;

        private static void Write(LogWriteHandler handler, IEnumerable<string> messages)
        {
            foreach (var message in messages)
                handler.WriteMessage(new LogEntry(message, [], LogMessageType.Verbose, "Test", "LiveLogTest", null));
        }

        /// <summary>
        /// Polls from the given ID until nothing new is returned, and returns the messages in the
        /// order they were written, as the web UI puts the pages together.
        /// </summary>
        private static List<string> PollUntilCaughtUp(LogWriteHandler handler, ref long id)
        {
            var messages = new List<string>();
            for (var i = 0; i < 100; i++)
            {
                var page = handler.AfterID(id, LogMessageType.Verbose, PageSize);
                if (page.Length == 0)
                    break;

                messages.AddRange(page.OrderBy(x => x.ID).Select(x => x.Message));
                id = page.Max(x => x.ID);
            }

            return messages;
        }

        [Test]
        public void MoreLinesThanAPageBetweenPollsAreAllReturned()
        {
            var handler = new LogWriteHandler();

            // The first poll turns the live log on, and the UI keeps the ID of what it saw
            long id = 0;
            handler.AfterID(id, LogMessageType.Verbose, PageSize);
            Write(handler, ["before"]);
            var first = PollUntilCaughtUp(handler, ref id);
            CollectionAssert.AreEqual(new[] { "before" }, first);

            // A burst of lines between two polls, such as a database recreate at Verbose
            var burst = Enumerable.Range(1, 250).Select(i => $"Processing filelist volume {i} of 250").ToList();
            Write(handler, burst);

            var received = PollUntilCaughtUp(handler, ref id);

            CollectionAssert.AreEqual(burst, received, "Lines were skipped between two polls");
        }

        [Test]
        public void TheFirstPollShowsTheNewestLines()
        {
            var handler = new LogWriteHandler();
            handler.AfterID(0, LogMessageType.Verbose, PageSize);

            var lines = Enumerable.Range(1, 250).Select(i => $"line {i}").ToList();
            Write(handler, lines);

            // A UI that has seen nothing yet starts from what is happening now, not from the
            // oldest line kept
            var page = handler.AfterID(0, LogMessageType.Verbose, PageSize);

            CollectionAssert.AreEqual(lines.Skip(150), page.OrderBy(x => x.ID).Select(x => x.Message));
        }
    }
}
