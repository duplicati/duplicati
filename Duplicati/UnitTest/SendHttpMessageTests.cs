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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests the requests the HTTP report module sends
    /// </summary>
    [TestFixture]
    [Category("ReportHelper")]
    public class SendHttpMessageTests
    {
        /// <summary>
        /// Runs the HTTP report module against a local server and returns the request header lines it received
        /// </summary>
        /// <param name="url">The report URL, with the server address written as {0}</param>
        /// <param name="extra">More options for the module</param>
        private static async Task<string[]> SendReportAsync(string url, Dictionary<string, string> extra = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var address = $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
                var received = Task.Run(async () =>
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    var stream = client.GetStream();
                    var text = new StringBuilder();
                    var buffer = new byte[8192];
                    while (!text.ToString().Contains("\r\n\r\n"))
                    {
                        var read = await stream.ReadAsync(buffer);
                        if (read == 0)
                            break;
                        text.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }

                    var headers = text.ToString().Split("\r\n\r\n")[0].Split("\r\n");
                    var authorized = headers.Any(x => x.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase));
                    var response = authorized
                        ? "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        : "HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"test\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
                    return headers;
                });

                var module = new SendHttpMessage();
                var options = new Dictionary<string, string>
                {
                    ["send-http-url"] = string.Format(url, address),
                    ["send-http-any-operation"] = "true",
                    ["send-http-retries"] = "1",
                };
                foreach (var kv in extra ?? new())
                    options[kv.Key] = kv.Value;
                module.Configure(options);

                var remoteurl = "file:///not-used";
                var paths = Array.Empty<string>();
                module.OnStart("Backup", ref remoteurl, ref paths);
                module.OnFinish(null, new Exception("Simulated failure"));

                Assert.That(await Task.WhenAny(received, Task.Delay(TimeSpan.FromSeconds(30))), Is.SameAs(received), "The report was not sent");
                return await received;
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public async Task UserInfoInTheUrlIsSentAsBasicAuthentication()
        {
            var headers = await SendReportAsync("http://report-user:p%40ss%3Aword@{0}/hook?id=1");

            var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("report-user:p@ss:word"));
            Assert.That(headers, Does.Contain("Authorization: " + expected));
            Assert.That(headers[0], Does.StartWith("POST /hook?id=1 HTTP/"));
        }

        [Test]
        public async Task UrlWithoutUserInfoSendsNoAuthentication()
        {
            var headers = await SendReportAsync("http://{0}/hook");

            Assert.IsFalse(headers.Any(x => x.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)));
            Assert.That(headers[0], Does.StartWith("POST /hook HTTP/"));
        }

        [Test]
        public async Task ExtraHeadersAreSent()
        {
            // #4025: a service that wants an API key in a header
            var headers = await SendReportAsync("http://{0}/hook", new() { ["send-http-headers"] = "{\"X-Api-Key\": \"abc123\", \"Authorization\": \"Bearer token-1\", \"Content-Type\": \"text/plain\"}" });

            Assert.That(headers, Does.Contain("X-Api-Key: abc123"));
            Assert.That(headers, Does.Contain("Authorization: Bearer token-1"));
            Assert.That(headers.Where(x => x.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(new[] { "Content-Type: text/plain" }), "A content header replaces the one the module sets");
        }

        [Test]
        public async Task AnExtraHeaderReplacesTheAuthenticationFromTheUrl()
        {
            var headers = await SendReportAsync("http://report-user:secret@{0}/hook", new() { ["send-http-headers"] = "{\"Authorization\": \"Bearer token-1\"}" });

            Assert.That(headers.Where(x => x.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(new[] { "Authorization: Bearer token-1" }));
        }

        [Test]
        public async Task HeadersThatCannotBeReadAreIgnored()
        {
            var headers = await SendReportAsync("http://report-user:secret@{0}/hook", new() { ["send-http-headers"] = "X-Api-Key: abc123" });

            Assert.That(headers.Any(x => x.StartsWith("X-Api-Key", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(headers[0], Does.StartWith("POST /hook HTTP/"), "The report is still sent");
        }

        [Test]
        public void HeaderNamesAreChecked()
        {
            Assert.That(SendHttpMessage.ParseHeaders("{\"Bad Name\": \"x\"}"), Is.Empty);
            Assert.That(SendHttpMessage.ParseHeaders("{\"X-Ok\": \"x\"}"), Is.EqualTo(new Dictionary<string, string> { ["X-Ok"] = "x" }));
            Assert.That(SendHttpMessage.ParseHeaders(""), Is.Empty);
        }

        [Test]
        public void OptionDescriptionsCanBeShownWithoutATranslation()
        {
            // The server lists every module option when it starts. Without a translation the
            // text is passed to string.Format, so a stray brace stops the server.
            // A temporary context cannot select the invariant culture (its name is empty)
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                foreach (var command in new SendHttpMessage().SupportedCommands)
                    Assert.DoesNotThrow(() => _ = command.LongDescription + command.ShortDescription, command.Name);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = culture;
            }
        }
    }
}
