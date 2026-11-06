#nullable enable
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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Runs a backup that sends its report to a local HTTP receiver, and checks that
    /// values containing regex substitution sequences ($&amp;, $1, $$, ...) arrive literally,
    /// and that template keys and values containing '%' or regex characters are not expanded again.
    /// </summary>
    [TestFixture]
    [Category("ReportHelper")]
    public class ReportTemplateDollarTests : BasicSetupHelper
    {
        /// <summary>
        /// Text holding every .NET regex substitution form that is also a valid file name
        /// </summary>
        private const string DOLLAR_TEXT = "dollar-$&-$1-$$-$`-$'-$_-end";

        /// <summary>
        /// A local HTTP endpoint that records the bodies it receives
        /// </summary>
        private sealed class Receiver : IDisposable
        {
            private readonly HttpListener m_listener = new();
            private readonly Task m_loop;
            public readonly List<string> Bodies = new();
            public string Url { get; }

            public Receiver()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();

                Url = $"http://127.0.0.1:{port}/";
                m_listener.Prefixes.Add(Url);
                m_listener.Start();
                m_loop = Task.Run(LoopAsync);
            }

            private async Task LoopAsync()
            {
                while (m_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await m_listener.GetContextAsync(); }
                    catch { return; }

                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        var body = await reader.ReadToEndAsync();
                        lock (Bodies)
                            Bodies.Add(body);
                    }

                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                }
            }

            public void Dispose()
            {
                m_listener.Stop();
                m_listener.Close();
                try { m_loop.Wait(TimeSpan.FromSeconds(5)); }
                catch { }
            }
        }

        /// <summary>
        /// The source folder, named so that the local path and the logged file paths carry <see cref="DOLLAR_TEXT"/>
        /// </summary>
        private string SourceFolder => Path.Combine(DATAFOLDER, DOLLAR_TEXT);

        /// <summary>
        /// Runs a backup of <see cref="SourceFolder"/> and returns the single report body received
        /// </summary>
        private async Task<string> BackupAndReceiveAsync(Dictionary<string, string> reportOptions, bool form, string fileName = DOLLAR_TEXT + ".txt")
        {
            Directory.CreateDirectory(SourceFolder);
            File.WriteAllText(Path.Combine(SourceFolder, fileName), "content");

            using var receiver = new Receiver();
            var options = new Dictionary<string, string>(TestOptions)
            {
                [form ? "send-http-form-urls" : "send-http-json-urls"] = receiver.Url,
                ["send-http-level"] = "All",
                ["send-http-log-level"] = "Verbose",
                ["send-http-max-log-lines"] = "0",
                ["send-http-retries"] = "0",
                ["allow-paths-in-log-messages"] = "true",
            };
            foreach (var kv in reportOptions)
                options[kv.Key] = kv.Value;

            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { SourceFolder }));

            string body;
            lock (receiver.Bodies)
            {
                Assert.That(receiver.Bodies, Has.Count.EqualTo(1), "Expected exactly one report");
                body = receiver.Bodies[0];
            }

            if (form)
            {
                Assert.That(body, Does.StartWith("message="));
                // The extra parameters follow the escaped message as further form fields
                body = Uri.UnescapeDataString(body.Substring("message=".Length).Split('&')[0]);
            }

            TestContext.Out.WriteLine(body);
            return body;
        }

        [Test]
        public async Task DuplicatiFormatResultKeepsDollarSequences()
        {
            var body = await BackupAndReceiveAsync(new Dictionary<string, string>(), form: true);

            Assert.That(body, Does.Contain(DOLLAR_TEXT + ".txt"), "The log line with the file name should arrive literally");
        }

        [Test]
        public async Task TemplateKeysKeepDollarSequences()
        {
            var body = await BackupAndReceiveAsync(new Dictionary<string, string>
            {
                ["send-http-message"] = "name=[%backup-name%] extra=[%custom-key%] path=[%localpath%]",
                ["backup-name"] = DOLLAR_TEXT,
                ["send-http-extra-parameters"] = $"{{\"custom-key\": \"{DOLLAR_TEXT}\"}}",
            }, form: true);

            Assert.That(body, Is.EqualTo($"name=[{DOLLAR_TEXT}] extra=[{DOLLAR_TEXT}] path=[{SourceFolder}]"));
        }

        [Test]
        public async Task TemplateFormatResultKeepsDollarSequences()
        {
            var body = await BackupAndReceiveAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Template",
            }, form: true);

            Assert.That(body, Does.Contain(DOLLAR_TEXT + ".txt"), "The log line with the file name should arrive literally");
        }

        [Test]
        public async Task ResultKeepsPercentSequences()
        {
            // A template key and a '%...%' pair inside a log line of the result
            const string fileName = "percent-%backup-name%-50%-x%-end.txt";
            var body = await BackupAndReceiveAsync(new Dictionary<string, string>
            {
                ["backup-name"] = "expanded",
            }, form: true, fileName: fileName);

            Assert.That(body, Does.Contain(fileName), "The log line with the file name should arrive literally");
        }

        [Test]
        public async Task TemplateKeysWithRegexCharactersMatchLiterally()
        {
            var body = await BackupAndReceiveAsync(new Dictionary<string, string>
            {
                ["send-http-message"] = "paren=[%a(b%] dot=[%a.c%] other=[%abc%]",
                ["send-http-extra-parameters"] = "{\"a(b\": \"paren\", \"a.c\": \"dot\"}",
            }, form: true);

            Assert.That(body, Is.EqualTo("paren=[paren] dot=[dot] other=[]"));
        }

        [Test]
        public async Task JsonFormatKeepsDollarSequences()
        {
            var body = await BackupAndReceiveAsync(new Dictionary<string, string>
            {
                ["backup-name"] = DOLLAR_TEXT,
            }, form: false);

            Assert.That(body, Does.Contain(DOLLAR_TEXT + ".txt"), "The log line with the file name should arrive literally");
            Assert.That(body, Does.Contain($"\"{DOLLAR_TEXT}\""), "The backup name should arrive literally");
        }
    }
}
