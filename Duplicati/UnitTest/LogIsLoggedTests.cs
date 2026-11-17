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
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Some log messages need extra work to produce, such as a query for the paths of deleted entries.
    /// <see cref="Log.IsLogged"/> tells if any destination captures a message, so that work can be skipped.
    /// </summary>
    [Category("Targeted")]
    public class LogIsLoggedTests
    {
        private const string TAG = "Test.FileEntry";
        private const string ID = "DeletedFile";

        private sealed class LogSink : ILogDestination
        {
            public List<LogEntry> Entries { get; } = [];

            public void WriteMessage(LogEntry entry) => Entries.Add(entry);
        }

        [Test]
        public void NothingIsLoggedWithoutADestination()
        {
            using (Log.StartIsolatingScope(true))
                Assert.IsFalse(Log.IsLogged(LogMessageType.Error, TAG, ID));
        }

        [Test]
        public void ScopeLogLevelDecides()
        {
            using (Log.StartIsolatingScope(true))
            using (Log.StartScope(new LogSink(), LogMessageType.Information))
            {
                Assert.IsFalse(Log.IsLogged(LogMessageType.Verbose, TAG, ID));
                Assert.IsTrue(Log.IsLogged(LogMessageType.Information, TAG, ID));

                // An outer destination is enough, and an isolating scope hides it again
                using (Log.StartScope(new LogSink(), LogMessageType.Warning))
                {
                    Assert.IsTrue(Log.IsLogged(LogMessageType.Information, TAG, ID));
                    using (Log.StartIsolatingScope(false))
                        Assert.IsFalse(Log.IsLogged(LogMessageType.Information, TAG, ID));
                }
            }
        }

        [Test]
        public void ScopeFilterDecides()
        {
            var sink = new LogSink();
            using (Log.StartIsolatingScope(true))
            using (Log.StartScope(sink.WriteMessage, x => x.Id == ID))
            {
                Assert.IsTrue(Log.IsLogged(LogMessageType.Verbose, TAG, ID));
                Assert.IsFalse(Log.IsLogged(LogMessageType.Verbose, TAG, "DeletedFolder"));
            }

            // Asking must not write anything
            Assert.AreEqual(0, sink.Entries.Count);
        }

        [Test]
        public void DecoratingScopeIsNotADestination()
        {
            var seen = 0;
            var sink = new LogSink();
            using (Log.StartIsolatingScope(true))
            using (Log.StartScope(sink, LogMessageType.Information))
            using (Log.StartDecoratingScope(x => { seen++; x["key"] = "value"; }))
            {
                Assert.IsFalse(Log.IsLogged(LogMessageType.Verbose, TAG, ID));
                Assert.AreEqual(0, seen);

                Log.WriteInformationMessage(TAG, ID, "message");
            }

            // The decorator still sees the messages that are written
            Assert.AreEqual(1, seen);
            Assert.AreEqual(1, sink.Entries.Count);
            Assert.AreEqual("value", sink.Entries[0]["key"]);
        }

        [Test]
        public void MultiLogTargetIsAskedForItsTargets()
        {
            using (Log.StartIsolatingScope(true))
            using (var target = new ControllerMultiLogTarget(new LogSink(), LogMessageType.Information, null, null))
            using (Log.StartScope(target, null))
            {
                // The scope has no filter, so the levels of the targets decide
                Assert.IsFalse(Log.IsLogged(LogMessageType.Verbose, TAG, ID));
                Assert.IsTrue(Log.IsLogged(LogMessageType.Information, TAG, ID));

                // A filter that includes the message wins over the level of the target
                target.AddTarget(new LogSink(), LogMessageType.Warning, FilterExpression.ParseLogFilter("+*-" + ID));
                Assert.IsTrue(Log.IsLogged(LogMessageType.Verbose, TAG, ID));
                Assert.IsFalse(Log.IsLogged(LogMessageType.Verbose, TAG, "DeletedFolder"));
            }
        }

        [Test]
        public void MultiLogTargetAsksNestedTargets()
        {
            using (Log.StartIsolatingScope(true))
            using (var inner = new ControllerMultiLogTarget(new LogSink(), LogMessageType.Warning, null, null))
            using (var outer = new ControllerMultiLogTarget(inner, LogMessageType.Verbose, null, null))
            using (Log.StartScope(outer, null))
            {
                Assert.IsFalse(Log.IsLogged(LogMessageType.Verbose, TAG, ID));
                Assert.IsTrue(Log.IsLogged(LogMessageType.Warning, TAG, ID));
            }
        }
    }
}
