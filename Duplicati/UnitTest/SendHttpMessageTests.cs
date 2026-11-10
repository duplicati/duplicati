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
        private static async Task<string[]> SendReportAsync(string url)
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
                module.Configure(new Dictionary<string, string>
                {
                    ["send-http-url"] = string.Format(url, address),
                    ["send-http-any-operation"] = "true",
                    ["send-http-retries"] = "1",
                });

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
    }
}
