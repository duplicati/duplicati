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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Modules.Builtin;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests the messages the Telegram report module sends
    /// </summary>
    [TestFixture]
    [Category("ReportHelper")]
    public class SendTelegramMessageTests
    {
        /// <summary>
        /// The longest text Telegram accepts in one message
        /// </summary>
        private const int TELEGRAM_MAX_LENGTH = 4096;

        /// <summary>
        /// Accepts every request without a network and keeps the text of each message
        /// </summary>
        private sealed class StubTelegram : HttpMessageHandler, IHttpClientFactory
        {
            public ConcurrentQueue<string> Texts { get; } = new();

            public HttpClient CreateClient(string name) => new HttpClient(this, false);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var text = request.RequestUri!.Query.TrimStart('?').Split('&').Single(x => x.StartsWith("text=", StringComparison.Ordinal));
                Texts.Enqueue(Uri.UnescapeDataString(text.Substring("text=".Length)));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}")
                });
            }
        }

        /// <summary>
        /// Gives the test access to the method that sends the report
        /// </summary>
        private sealed class TestableTelegramModule : SendTelegramMessage
        {
            public TestableTelegramModule()
            {
                ConfigureModule(new Dictionary<string, string>
                {
                    ["send-telegram-channel-id"] = "1",
                    ["send-telegram-bot-id"] = "1",
                    ["send-telegram-api-key"] = "test"
                });
            }

            public void Send(string body) => SendMessage(string.Empty, body);
        }

        private StubTelegram m_telegram;

        [SetUp]
        public void SetUp()
        {
            m_telegram = new StubTelegram();
            HttpClientHelper.Configure(m_telegram);
        }

        [TearDown]
        public void TearDown()
        {
            HttpClientHelper.Unconfigure(m_telegram);
            m_telegram.Dispose();
        }

        /// <summary>
        /// Removes the "Part n/t:" line from a message
        /// </summary>
        private static string WithoutPartHeader(string text)
            => text.Substring(text.IndexOf('\n') + 1);

        /// <summary>
        /// Sends a report and returns what the module sent and the texts Telegram was given
        /// </summary>
        /// <param name="body">The report body</param>
        private (string Sent, string[] Texts) SendReport(string body)
        {
            // The module puts the empty subject line before the body
            var sent = Environment.NewLine + body;
            new TestableTelegramModule().Send(body);

            // The module does not report when it is done, so wait for the whole report to arrive
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                var texts = m_telegram.Texts.ToArray();
                return (texts.Length == 1 ? texts[0].Length : texts.Sum(x => WithoutPartHeader(x).Length)) >= sent.Length;
            }, TimeSpan.FromSeconds(10)), "The report was not sent in full");

            return (sent, m_telegram.Texts.ToArray());
        }

        /// <summary>
        /// Builds a report body that makes the sent message the requested length
        /// </summary>
        /// <param name="length">The length of the message, counting the empty subject line</param>
        private static string CreateBody(int length)
        {
            var sb = new StringBuilder();
            for (var i = 0; sb.Length < length; i++)
                sb.Append($"Line {i}: synthetic report text\n");

            return sb.ToString(0, length - Environment.NewLine.Length);
        }

        [TestCase(100)]
        [TestCase(TELEGRAM_MAX_LENGTH)]
        public void AReportWithinTheLimitIsSentAsOneMessage(int length)
        {
            var (sent, texts) = SendReport(CreateBody(length));

            Assert.AreEqual(1, texts.Length);
            Assert.AreEqual(sent, texts[0]);
        }

        [TestCase(TELEGRAM_MAX_LENGTH + 1)]
        [TestCase(2 * TELEGRAM_MAX_LENGTH)]
        [TestCase(100 * TELEGRAM_MAX_LENGTH)]
        public void EveryPartOfALongReportIsWithinTheLimit(int length)
        {
            var (sent, texts) = SendReport(CreateBody(length));

            Assert.Greater(texts.Length, 1);
            for (var i = 0; i < texts.Length; i++)
            {
                Assert.IsTrue(texts[i].StartsWith($"Part {i + 1}/{texts.Length}:\n", StringComparison.Ordinal), $"Part {i + 1}/{texts.Length} has the wrong header");
                Assert.LessOrEqual(texts[i].Length, TELEGRAM_MAX_LENGTH, $"Part {i + 1}/{texts.Length} is too long");
            }

            Assert.AreEqual(sent, string.Concat(texts.Select(WithoutPartHeader)));
        }
    }
}
