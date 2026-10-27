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
using System.Linq;
using Duplicati.Library.Logging;
using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Checks that the XMPP report module sends to every recipient in send-xmpp-to
    /// </summary>
    [TestFixture]
    [Category("XmppTarget")]
    public class XmppRecipientsTests
    {
        private sealed class LogSink : ILogDestination
        {
            public List<LogEntry> Entries { get; } = new();

            public void WriteMessage(LogEntry entry)
            {
                lock (Entries)
                    Entries.Add(entry);
            }
        }

        [Test]
        public void FailingRecipientDoesNotStopTheNextOne()
        {
            var attempted = new List<string>();
            var sink = new LogSink();
            using (Log.StartScope(sink, LogMessageType.Warning))
            {
                SendJabberMessage.SendToRecipients("first@example.com,second@example.com", jid =>
                {
                    attempted.Add(jid.ToString());
                    if (jid.Node == "first")
                        throw new InvalidOperationException("Simulated read failure");
                });
            }

            Assert.That(attempted, Is.EqualTo(new[] { "first@example.com", "second@example.com" }),
                "The message was not sent to the recipient after the failing one");
            var warning = sink.Entries.Single(x => x.Id == "XMPPSendError");
            Assert.That(warning.FormattedMessage, Does.Contain("first@example.com"));
        }

        [Test]
        public void EveryRecipientIsSentTo()
        {
            var attempted = new List<string>();
            var sink = new LogSink();
            using (Log.StartScope(sink, LogMessageType.Warning))
                SendJabberMessage.SendToRecipients("first@example.com,,second@example.com", jid => attempted.Add(jid.ToString()));

            Assert.That(attempted, Is.EqualTo(new[] { "first@example.com", "second@example.com" }));
            Assert.That(sink.Entries, Is.Empty);
        }
    }
}
