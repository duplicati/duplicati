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

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The reports sent after an operation (HTTP, email, ...) run after the result has been written
    /// to the job log. A report that fails to send turns the operation into a warning, but the
    /// warning was missing from the job log, so the log showed nothing to explain it.
    /// </summary>
    [TestFixture]
    public class ReportResultLogTests : BasicSetupHelper
    {
        /// <summary>
        /// Runs a backup with the given extra options and returns the result together with the
        /// results written to the job log.
        /// </summary>
        private async Task<(IBackupResults Result, List<JObject> Logged)> BackupAndReadResultLogAsync(Dictionary<string, string> extra)
        {
            var testopts = TestOptions.Expand(new { no_encryption = true });
            foreach (var kv in extra)
                testopts[kv.Key] = kv.Value;

            Directory.CreateDirectory(Path.Combine(DATAFOLDER, "folder"));
            File.WriteAllText(Path.Combine(DATAFOLDER, "folder", "file.txt"), "some data");

            IBackupResults result;
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                result = await c.BackupAsync([DATAFOLDER]);

            var logged = new List<JObject>();
            await using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
            await using (var cmd = con.CreateCommand())
            {
                cmd.SetCommandAndParameters(@"SELECT ""Message"" FROM ""LogData"" WHERE ""Type"" = 'Result'");
                await using (var rd = await cmd.ExecuteReaderAsync())
                    while (await rd.ReadAsync())
                        logged.Add(JObject.Parse(rd.ConvertValueToString(0) ?? ""));
            }

            Assert.That(logged, Has.Count.EqualTo(1), "The backup should write exactly one result to the job log");
            return (result, logged);
        }

        /// <summary>
        /// A report that cannot be delivered is a warning on the operation, and the job log must show it.
        /// </summary>
        [Test]
        [Category("Serialization")]
        public async Task AFailedReportIsShownInTheJobLogAsync()
        {
            // Nothing listens on the discard port, so the connection is refused
            var (result, logged) = await BackupAndReadResultLogAsync(new Dictionary<string, string>
            {
                ["send-http-json-urls"] = "http://127.0.0.1:9/",
                ["send-http-retries"] = "1",
                ["send-http-retry-delay"] = "0s",
            });

            Assert.That(result.Warnings, Has.Some.Contains("ReportSubmitError"), "The report did not fail as the test expects");

            var warnings = logged[0]["Warnings"]!.Select(x => (string?)x).ToList();
            Assert.That(warnings, Has.Some.Contains("ReportSubmitError"),
                $"The job log does not show that the report failed:{System.Environment.NewLine}{logged[0]}");
            Assert.That((long)logged[0]["WarningsActualLength"]!, Is.EqualTo(result.Warnings.Count()),
                "The warning count in the job log does not match the result");
            Assert.That((string?)logged[0]["ParsedResult"], Is.EqualTo(nameof(ParsedResultType.Warning)));
        }

        /// <summary>
        /// A report that is delivered adds nothing to the result, so the job log must stay as it was
        /// written. Auto-vacuum runs after the result is written, so a rewrite would add its results.
        /// </summary>
        [Test]
        [Category("Serialization")]
        public async Task ADeliveredReportLeavesTheJobLogAloneAsync()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cts = new CancellationTokenSource();
            var server = AnswerOkAsync(listener, cts.Token);

            var (result, logged) = await BackupAndReadResultLogAsync(new Dictionary<string, string>
            {
                ["send-http-json-urls"] = $"http://127.0.0.1:{port}/",
                ["send-http-retries"] = "1",
                ["send-http-retry-delay"] = "0s",
                ["auto-vacuum"] = "true",
            });

            cts.Cancel();
            var requests = await server;

            Assert.That(requests, Is.EqualTo(1), "The report was not delivered");
            TestUtils.AssertResults(result);
            Assert.That(result.VacuumResults, Is.Not.Null, "Auto-vacuum did not run");
            Assert.That(logged[0]["VacuumResults"]!.Type, Is.EqualTo(JTokenType.Null),
                $"The result in the job log was rewritten after a delivered report:{System.Environment.NewLine}{logged[0]}");
        }

        /// <summary>
        /// Answers each HTTP request with 200 OK until cancelled, and returns how many it answered.
        /// </summary>
        private static async Task<int> AnswerOkAsync(TcpListener listener, CancellationToken token)
        {
            var count = 0;
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(token);
                    using var stream = client.GetStream();
                    var request = new StringBuilder();
                    var buffer = new byte[8192];
                    var headerEnd = -1;
                    var contentLength = 0;
                    while (true)
                    {
                        var read = await stream.ReadAsync(buffer, token);
                        if (read == 0)
                            break;
                        request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        var text = request.ToString();
                        if (headerEnd < 0)
                        {
                            headerEnd = text.IndexOf("\r\n\r\n", System.StringComparison.Ordinal);
                            if (headerEnd >= 0)
                            {
                                var line = text[..headerEnd].Split("\r\n")
                                    .FirstOrDefault(x => x.StartsWith("Content-Length:", System.StringComparison.OrdinalIgnoreCase));
                                if (line != null)
                                    contentLength = int.Parse(line["Content-Length:".Length..].Trim());
                            }
                        }
                        if (headerEnd >= 0 && Encoding.ASCII.GetByteCount(text) >= headerEnd + 4 + contentLength)
                            break;
                    }

                    var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, token);
                    count++;
                }
            }
            catch (System.OperationCanceledException)
            {
            }

            return count;
        }
    }
}
