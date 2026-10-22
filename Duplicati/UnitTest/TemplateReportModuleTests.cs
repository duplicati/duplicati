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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.Modules.Builtin;
using Duplicati.Library.ResultSerialization;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The Template result format, as the send-http module delivers it to a receiver
    /// </summary>
    [TestFixture]
    [Category("ReportModule")]
    public class TemplateReportModuleTests
    {
        /// <summary>
        /// A finished backup with a fixed result
        /// </summary>
        private sealed class SuccessfulResults : IBasicResults
        {
            public DateTime BeginTime => new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);
            public DateTime EndTime => BeginTime.AddMinutes(1);
            public TimeSpan Duration => EndTime - BeginTime;
            public IEnumerable<string> Errors => [];
            public IEnumerable<string> Warnings => [];
            public IEnumerable<string> Messages => [];
            public ParsedResultType ParsedResult => ParsedResultType.Success;
            public bool Interrupted => false;
        }

        /// <summary>
        /// Runs a backup through the send-http module and returns the message a local receiver got
        /// </summary>
        private static async Task<string> ReceiveReportAsync(Dictionary<string, string> options)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var prefix = $"http://localhost:{port}/";
            using var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            var received = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync();
                using var reader = new StreamReader(context.Request.InputStream);
                var form = await reader.ReadToEndAsync();
                context.Response.StatusCode = 200;
                context.Response.Close();
                return form;
            });

            options["send-http-url"] = prefix;
            options["send-http-retries"] = "0";

            var module = new SendHttpMessage();
            module.Configure(options);
            var url = "file:///not-used";
            var paths = Array.Empty<string>();
            module.OnStart("Backup", ref url, ref paths);
            module.OnFinish(new SuccessfulResults(), null);

            Assert.That(await Task.WhenAny(received, Task.Delay(TimeSpan.FromSeconds(30))), Is.SameAs(received), "The receiver got no report");
            var body = await received;
            Assert.That(body, Does.StartWith("message="));
            // The templates are checked out with the line endings of the system, so compare with "\n" only
            return Uri.UnescapeDataString(body.Substring("message=".Length)).Replace("\r\n", "\n");
        }

        [Test]
        public async Task TemplateReportUsesTheEmbeddedDefaultTemplate()
        {
            var message = await ReceiveReportAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Template",
                ["send-http-message"] = "%RESULT%"
            });

            // default.hbs has a "Results:" section and labels the backup "Backup:";
            // the built-in fallback template has neither
            Assert.That(message, Does.Contain("Results:\n--------"), message);
            Assert.That(message, Does.Not.Contain("Backup Name:"), message);
        }

        [Test]
        public async Task TemplateReportFillsInTheOperationValues()
        {
            var message = await ReceiveReportAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Template",
                ["send-http-message"] = "%RESULT%",
                ["backup-name"] = "Documents"
            });

            Assert.That(message, Does.StartWith("Duplicati Backup Report\n"), message);
            Assert.That(message, Does.Contain("Status: Success\n"), message);
            Assert.That(message, Does.Contain("Backup: Documents\n"), message);
            Assert.That(message, Does.Contain($"Machine: {DataFolderManager.MachineName}\n"), message);
        }

        [Test]
        public void AllBuiltInTemplatesAreListed()
        {
            Assert.That(TemplateFormatSerializer.GetAvailableTemplates(), Is.EquivalentTo(new[] { "default", "email", "html", "slack" }));
        }
    }
}
