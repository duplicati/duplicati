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
        /// The destination statistics of <see cref="BackupShapedResults"/>
        /// </summary>
        private sealed class BackendShapedStatistics
        {
            public long BytesUploaded => 54421095;
            public long RetryAttempts => 0;
        }

        /// <summary>
        /// The verification results of <see cref="BackupShapedResults"/>
        /// </summary>
        private sealed class TestShapedResults
        {
            public int VerificationsActualLength => 20;
            public TimeSpan Duration => new TimeSpan(0, 6, 51);
        }

        /// <summary>
        /// A finished backup with the values the summary template reads
        /// </summary>
        private sealed class BackupShapedResults : IBasicResults
        {
            public DateTime BeginTime => new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);
            public DateTime EndTime => BeginTime.Add(Duration);
            public TimeSpan Duration => new TimeSpan(0, 26, 30);
            public IEnumerable<string> Errors => [];
            public IEnumerable<string> Warnings => [];
            public IEnumerable<string> Messages => [];
            public ParsedResultType ParsedResult => ParsedResultType.Success;
            public bool Interrupted => false;

            public long SizeOfAddedFiles => 60000000;
            public long SizeOfModifiedFiles => 41502157;
            public long AddedFiles => 115;
            public long ModifiedFiles => 128;
            public long DeletedFiles => 108;
            public long ExaminedFiles => 365756;
            public long AddedFolders => 5;
            public long ModifiedFolders => 49;
            public long DeletedFolders => 1;
            public long FilesWithError => 0;
            public long NotProcessedFiles => 1;
            public bool PartialBackup => false;
            public bool Dryrun => false;
            public BackendShapedStatistics BackendStatistics => new BackendShapedStatistics();
            public TestShapedResults TestResults => new TestShapedResults();
        }

        /// <summary>
        /// Runs a backup through the send-http module and returns the message a local receiver got
        /// </summary>
        private static Task<string> ReceiveReportAsync(Dictionary<string, string> options)
            => ReceiveReportAsync(options, new SuccessfulResults());

        /// <summary>
        /// Runs a backup with the given result through the send-http module and returns the message a local receiver got
        /// </summary>
        private static async Task<string> ReceiveReportAsync(Dictionary<string, string> options, IBasicResults results)
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
            module.OnFinish(results, null);

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
            Assert.That(TemplateFormatSerializer.GetAvailableTemplates(), Is.EquivalentTo(new[] { "default", "email", "html", "slack", "summary" }));
        }

        [Test]
        public async Task TemplateOptionPicksTheTemplateForTheResult()
        {
            var message = await ReceiveReportAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Template",
                ["send-http-result-template"] = "Email",
                ["send-http-message"] = "Report for %backup-name%: %RESULT%",
                ["backup-name"] = "Documents"
            });

            Assert.That(message, Does.StartWith("Report for Documents: Duplicati Backup Report: Success\n"), message);
        }

        [Test]
        public async Task TemplateOptionWithAnHtmlTemplateMakesTheBodyHtml()
        {
            var message = await ReceiveReportAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Template",
                ["send-http-result-template"] = "summary",
                ["send-http-message"] = "<p>%backup-name%</p>%RESULT%",
                ["backup-name"] = "Docs & Homes"
            });

            // The values put into the body are encoded, and the template is not encoded again
            Assert.That(message, Does.StartWith("<p>Docs &amp; Homes</p><!DOCTYPE html>"), message);
            Assert.That(message, Does.Contain("<h1>Backup summary &mdash; Docs &amp; Homes</h1>"), message);
        }

        [Test]
        public async Task BodyWithTheNameOfABundledTemplateIsText()
        {
            var message = await ReceiveReportAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Template",
                ["send-http-message"] = "email"
            });

            Assert.That(message, Is.EqualTo("email"));
        }

        [Test]
        public async Task TemplateFileGivenAsBodyIsUsedInsteadOfTheTemplateOption()
        {
            var templateFile = Path.Combine(Path.GetTempPath(), $"duplicati-template-{Guid.NewGuid():N}.hbs");
            try
            {
                File.WriteAllText(templateFile, "Custom {{OperationName}}");
                var message = await ReceiveReportAsync(new Dictionary<string, string>
                {
                    ["send-http-result-output-format"] = "Template",
                    ["send-http-result-template"] = "summary",
                    ["send-http-message"] = templateFile
                });

                Assert.That(message, Is.EqualTo("Custom Backup"));
            }
            finally
            {
                File.Delete(templateFile);
            }
        }

        [Test]
        public async Task UnknownTemplateOptionUsesTheDefaultTemplate()
        {
            var message = await ReceiveReportAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Template",
                ["send-http-result-template"] = "no-such-template",
                ["send-http-message"] = "%RESULT%"
            });

            Assert.That(message, Does.Contain("Results:\n--------"), message);
        }

        [Test]
        public async Task TemplateOptionIsNotUsedInOtherFormats()
        {
            var message = await ReceiveReportAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Duplicati",
                ["send-http-result-template"] = "summary",
                ["send-http-message"] = "%RESULT%"
            });

            Assert.That(message, Does.Not.Contain("<html>"), message);
            Assert.That(message, Does.Contain("ParsedResult: Success"), message);
        }

        [Test]
        public void ReportModulesListTheBundledTemplatesAsValues()
        {
            var modules = new (IGenericModule Module, string Option)[]
            {
                (new SendMail(), "send-mail-result-template"),
                (new SendHttpMessage(), "send-http-result-template"),
                (new SendTelegramMessage(), "send-telegram-result-template"),
                (new SendJabberMessage(), "send-xmpp-result-template")
            };

            foreach (var (module, option) in modules)
            {
                var argument = module.SupportedCommands.Single(x => x.Name == option);
                Assert.That(argument.Type, Is.EqualTo(CommandLineArgument.ArgumentType.Enumeration), option);
                Assert.That(argument.ValidValues, Is.EquivalentTo(TemplateFormatSerializer.GetAvailableTemplates()), option);
                Assert.That(argument.DefaultValue, Is.EqualTo("default"), option);
            }
        }

        [Test]
        public async Task BodyWithThePathOfATemplateFilePicksThatTemplate()
        {
            var templateFile = Path.Combine(Path.GetTempPath(), $"duplicati-template-{Guid.NewGuid():N}.hbs");
            try
            {
                File.WriteAllText(templateFile, "Custom {{OperationName}} of {{BackupName}}: {{ParsedResult}}, 100% done");
                var message = await ReceiveReportAsync(new Dictionary<string, string>
                {
                    ["send-http-result-output-format"] = "Template",
                    ["send-http-message"] = templateFile,
                    ["backup-name"] = "Documents"
                });

                Assert.That(message, Is.EqualTo("Custom Backup of Documents: Success, 100% done"));
            }
            finally
            {
                File.Delete(templateFile);
            }
        }

        [Test]
        public async Task BodyWithAMissingTemplateFileUsesTheDefaultTemplate()
        {
            var templateFile = Path.Combine(Path.GetTempPath(), $"duplicati-template-{Guid.NewGuid():N}.hbs");
            var message = await ReceiveReportAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Template",
                ["send-http-message"] = templateFile
            });

            Assert.That(message, Does.Contain("Results:\n--------"), message);
            Assert.That(message, Does.Not.Contain(Path.GetFileName(templateFile)), message);
        }

        [Test]
        public async Task SummaryTemplateRendersTheBackupAsHtml()
        {
            var results = new BackupShapedResults();
            var message = await ReceiveReportAsync(new Dictionary<string, string>
            {
                ["send-http-result-output-format"] = "Template",
                ["send-http-result-template"] = "summary",
                ["send-http-message"] = "%RESULT%",
                ["backup-name"] = "Docs & <Homes>"
            }, results);

            Assert.That(message, Does.StartWith("<!DOCTYPE html>"), message);
            Assert.That(message.TrimEnd(), Does.EndWith("</html>"), message);

            // The values are encoded by the template, once
            Assert.That(message, Does.Contain("<h1>Backup summary &mdash; Docs &amp; &lt;Homes&gt;</h1>"), message);

            var completed = results.EndTime.ToLocalTime().ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(message, Does.Contain($"<strong>Status: Successful</strong> &mdash; completed on {completed}, with no reported errors or warnings."), message);

            Assert.That(message, Does.Contain("<td>Data added or changed</td><td class=\"value\">96.8 MB</td>"), message);
            Assert.That(message, Does.Contain("<td>Files added / modified</td><td class=\"value\">115 / 128</td>"), message);
            Assert.That(message, Does.Contain("<td>Files deleted</td><td class=\"value\">108</td>"), message);
            Assert.That(message, Does.Contain("<td>Files examined</td><td class=\"value\">365,756</td>"), message);
            Assert.That(message, Does.Contain("<td>Total run time</td><td class=\"value\">26 minutes 30 seconds</td>"), message);
            Assert.That(message, Does.Contain("<td>Data uploaded to destination</td><td class=\"value\">51.9 MB</td>"), message);
            Assert.That(message, Does.Contain("The backup also added 5 folders, modified 49, and deleted 1."), message);

            Assert.That(message, Does.Contain("<li>0 files with errors and 1 file skipped.</li>"), message);
            Assert.That(message, Does.Contain("<li>Verification completed successfully for 20 backup files.</li>"), message);
            Assert.That(message, Does.Contain("<li>The verification step took about 6 minutes 51 seconds.</li>"), message);
            Assert.That(message, Does.Contain("<li>No transfers to or from the destination were retried.</li>"), message);
            Assert.That(message, Does.Contain("<strong>What this means:</strong> The operation completed normally."), message);
        }
    }
}
