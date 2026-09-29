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
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Modules.Builtin;
using Duplicati.Library.UsageReporter;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// With reduced reporting, the report modules send log message ids only.
    /// </summary>
    [TestFixture]
    [Category("ReportModule")]
    public class ReducedReportModuleTests
    {
        private const string SecretPath = "C:\\Patients\\John Doe\\MRI.pdf";
        private static readonly Regex LogLineRule = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{2} - \[[^\]\s:]+\](?: \[ex:[^\]\s:]+\](?: \[st:[A-Za-z0-9_.+`<>|$]+(?:;[A-Za-z0-9_.+`<>|$]+){0,15}\])?){0,5}$");
        private static readonly HashSet<string> AllowedExtraKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "report-mode", "machine-id", "backup-id", "backup-name", "machine-name", "operating-system", "operating-system-detailed",
            "installation-type", "destination-type", "destination-host-suffix", "next-scheduled-run", "update-channel"
        };

        /// <summary>
        /// A report module that records what it would have sent
        /// </summary>
        private sealed class RecordingReportModule : ReportHelper
        {
            public readonly List<(string Subject, string Body)> Sent = new();

            public override string Key => "test-report";
            public override string DisplayName => "Test report";
            public override string Description => "Report module used for testing";
            public override bool LoadAsDefault => false;
            public override IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            protected override string SubjectOptionName => "test-subject";
            protected override string BodyOptionName => "test-body";
            protected override string ActionLevelOptionName => "test-level";
            protected override string ActionOnAnyOperationOptionName => "test-any-operation";
            protected override string ActionOnOperationsOptionName => "test-operations";
            protected override string LogLevelOptionName => "test-log-level";
            protected override string LogFilterOptionName => "test-log-filter";
            protected override string LogLinesOptionName => "test-max-log-lines";
            protected override string ResultFormatOptionName => "test-result-format";
            protected override string ExtraDataOptionName => "test-extra-parameters";

            protected override bool ConfigureModule(IDictionary<string, string> commandlineOptions) => true;
            protected override void SendMessage(string subject, string body) => Sent.Add((subject, body));
        }

        /// <summary>
        /// Runs a backup-shaped operation through a module that logs a warning with a path and fails with a path in the exception
        /// </summary>
        private static (string Subject, string Body) RunReport(Dictionary<string, string> options)
        {
            var module = new RecordingReportModule();
            module.Configure(options);

            var url = "file:///not-used";
            var paths = new[] { SecretPath };
            module.OnStart("Backup", ref url, ref paths);
            Log.WriteWarningMessage("Duplicati.Library.Main.Operation.Backup.LogExceptionHelper", "PermissionDenied", new UnauthorizedAccessException($"Access to {SecretPath} denied"), "Excluding path due to permission denied: {0}", SecretPath);
            module.OnFinish(null, new UserInformationException($"Source folder {SecretPath} is missing", "MissingSourceFolder"));

            Assert.That(module.Sent, Has.Count.EqualTo(1));
            return module.Sent.Single();
        }

        [Test]
        public void JsonReportIsReduced()
        {
            var (_, body) = RunReport(new Dictionary<string, string>
            {
                ["reduced-reporting"] = "true",
                ["test-result-format"] = "Json",
                ["test-extra-parameters"] = "ticket=Patient John Doe",
                ["backup-name"] = "Documents"
            });

            Assert.That(body, Does.Not.Contain("John Doe"));
            Assert.That(body, Does.Not.Contain("denied"));

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.That(root.GetProperty("Data").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("Exception").GetString(), Is.EqualTo("Duplicati.Library.Interface.UserInformationException [helpid:MissingSourceFolder]"));

            var extra = root.GetProperty("Extra");
            Assert.That(extra.GetProperty("report-mode").GetString(), Is.EqualTo("reduced"));
            Assert.That(extra.GetProperty("backup-name").GetString(), Is.EqualTo("Documents"));
            foreach (var property in extra.EnumerateObject())
                Assert.That(AllowedExtraKeys, Does.Contain(property.Name), property.Name);

            var lines = root.GetProperty("LogLines").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.That(lines, Is.Not.Empty);
            foreach (var line in lines)
                Assert.That(line, Does.Match(LogLineRule), line);
            Assert.That(lines, Has.Some.Contains("[Warning-Duplicati.Library.Main.Operation.Backup.LogExceptionHelper-PermissionDenied] [ex:System.UnauthorizedAccessException]"));
        }

        [Test]
        public void TextReportIsReduced()
        {
            var (subject, body) = RunReport(new Dictionary<string, string>
            {
                ["reduced-reporting"] = "true",
                ["test-result-format"] = "Duplicati",
                ["test-subject"] = "Duplicati %OPERATIONNAME% report for %backup-name% at %LOCALPATH%",
                ["test-body"] = "%RESULT%\nSources: %LOCALPATH%\nTarget: %REMOTEURL%\nTicket: %ticket%",
                ["backup-name"] = "Documents",
                ["ticket"] = "Patient John Doe"
            });

            Assert.That(subject, Does.Not.Contain("John Doe"));
            Assert.That(subject, Does.Contain("Documents"));
            Assert.That(body, Does.Not.Contain("John Doe"));
            Assert.That(body, Does.Not.Contain("denied"));
            Assert.That(body, Does.Not.Contain("missing"));
            Assert.That(body, Does.Contain("UserInformationException"));
            Assert.That(body, Does.Contain("[Warning-Duplicati.Library.Main.Operation.Backup.LogExceptionHelper-PermissionDenied]"));
        }

        [Test]
        public void FullReportStillCarriesTextWhenNotReduced()
        {
            var (_, body) = RunReport(new Dictionary<string, string>
            {
                ["test-result-format"] = "Json",
                ["allow-paths-in-log-messages"] = "true"
            });

            Assert.That(body, Does.Contain("denied"));
            using var json = JsonDocument.Parse(body);
            Assert.That(json.RootElement.TryGetProperty("Extra", out var extra) && extra.TryGetProperty("report-mode", out _), Is.False);
        }

        [Test]
        public void ReducedLogLinesHaveNoTruncationMarker()
        {
            var module = new RecordingReportModule();
            module.Configure(new Dictionary<string, string>
            {
                ["reduced-reporting"] = "true",
                ["test-result-format"] = "Json",
                ["test-max-log-lines"] = "2"
            });
            var url = "file:///not-used";
            var paths = Array.Empty<string>();
            module.OnStart("Backup", ref url, ref paths);
            for (var i = 0; i < 5; i++)
                Log.WriteWarningMessage("Tag", "Id", null, "warning {0}", i);
            module.OnFinish(null, new Exception("x"));

            using var json = JsonDocument.Parse(module.Sent.Single().Body);
            var lines = json.RootElement.GetProperty("LogLines").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.That(lines, Has.Count.EqualTo(2));
            foreach (var line in lines)
                Assert.That(line, Does.Match(LogLineRule), line);
        }

        /// <summary>
        /// Captures the status reports instead of posting them
        /// </summary>
        private sealed class CapturingHttpReportStatus : HttpReportStatus
        {
            public List<HttpReportStatus.StatusReport> Reports { get; } = new();
            private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

            protected override Task SendAsync(string url, string json, CancellationToken cancellationToken)
            {
                Reports.Add(JsonSerializer.Deserialize<HttpReportStatus.StatusReport>(json, Options)!);
                return Task.CompletedTask;
            }
        }

        private static CapturingHttpReportStatus CreateStatusModule(bool reduced)
        {
            var module = new CapturingHttpReportStatus();
            var options = new Dictionary<string, string>
            {
                ["http-report-status-url"] = "http://localhost/example",
                ["http-report-status-max-log-lines"] = "20"
            };
            if (reduced)
                options["reduced-reporting"] = "true";
            module.Configure(options);
            return module;
        }

        [Test]
        public async Task StatusReportIsReducedAsync()
        {
            using var module = CreateStatusModule(reduced: true);
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnLogEntryAsync(new ReportLogEntry($"Excluding {SecretPath}", "Warning", "Warning-Duplicati.Tag-PermissionDenied", "PermissionDenied", DateTime.UtcNow, $"System.UnauthorizedAccessException: Access to {SecretPath} denied\n   at Foo.Bar()"), CancellationToken.None);
            await module.OnProgressTickAsync(new ReportProgressSnapshot("Backup_ProcessingFiles", 0.5f, 3, 100, 10, 1000, false, SecretPath, 100, 50, Array.Empty<ReportBackendEvent>()), CancellationToken.None);
            await module.OnOperationCompletedAsync(null!, new System.IO.IOException($"Could not find file '{SecretPath}'"), CancellationToken.None);

            var completed = module.Reports.Last();
            Assert.That(completed.Status, Is.EqualTo("Failed"));
            Assert.That(completed.ErrorMessage, Is.EqualTo("System.IO.IOException"));
            Assert.That(completed.Progress?.CurrentFilename, Is.Null);
            Assert.That(completed.LogEntries, Is.EqualTo(1), "counters are kept");
            Assert.That(completed.RecentLogLines, Has.Count.EqualTo(1));
            Assert.That(completed.RecentLogLines[0], Does.Match(LogLineRule));
            Assert.That(completed.RecentLogLines[0], Does.EndWith("[Warning-Duplicati.Tag-PermissionDenied] [ex:System.UnauthorizedAccessException]"));

            var serialized = JsonSerializer.Serialize(module.Reports);
            Assert.That(serialized, Does.Not.Contain("John Doe"));
        }

        [Test]
        public async Task StatusReportCarriesOnlyTheErrorCountOfTheResultsAsync()
        {
            using var module = CreateStatusModule(reduced: true);
            var results = new Duplicati.Library.Main.RestoreResults();
            results.WriteMessage(new LogEntry($"Failed to restore file {SecretPath}", [], LogMessageType.Error, "Test", "TestError", null));

            await module.OnOperationStartedAsync("Restore", null!, CancellationToken.None);
            await module.OnOperationCompletedAsync(results, null, CancellationToken.None);

            // The error itself is message text, so only the number of errors is reported
            var completed = module.Reports.Last();
            Assert.That(completed.Status, Is.EqualTo("Completed"));
            Assert.That(completed.ErrorMessage, Is.EqualTo("Got 1 error(s)"));

            var serialized = JsonSerializer.Serialize(module.Reports);
            Assert.That(serialized, Does.Not.Contain("Failed to restore"));
            Assert.That(serialized, Does.Not.Contain("John Doe"));
        }

        [Test]
        public async Task StatusReportModuleOptionCannotSwitchOffGlobalReducedReportingAsync()
        {
            using var module = new CapturingHttpReportStatus();
            module.Configure(new Dictionary<string, string>
            {
                ["http-report-status-url"] = "http://localhost/example",
                ["reduced-reporting"] = "true",
                ["http-report-status-reduced-reporting"] = "false",
                ["http-report-status-allow-paths-in-log-messages"] = "true"
            });

            Assert.That(module.RequestedLogContent, Is.EqualTo(ReportLogContent.ReducedMessage));

            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnLogEntryAsync(new ReportLogEntry($"Excluding {SecretPath}", "Warning", "Warning-Duplicati.Tag-PermissionDenied", "PermissionDenied", DateTime.UtcNow, null), CancellationToken.None);
            await module.OnProgressTickAsync(new ReportProgressSnapshot("Backup_ProcessingFiles", 0.5f, 3, 100, 10, 1000, false, SecretPath, 100, 50, Array.Empty<ReportBackendEvent>()), CancellationToken.None);
            await module.OnOperationCompletedAsync(null!, new System.IO.IOException($"Could not find file '{SecretPath}'"), CancellationToken.None);

            var completed = module.Reports.Last();
            Assert.That(completed.ErrorMessage, Is.EqualTo("System.IO.IOException"));
            Assert.That(completed.Progress?.CurrentFilename, Is.Null);
            Assert.That(JsonSerializer.Serialize(module.Reports), Does.Not.Contain("John Doe"));
        }

        [Test]
        public void StatusReportModuleOptionSwitchesReducedReportingOn()
        {
            using var module = new CapturingHttpReportStatus();
            module.Configure(new Dictionary<string, string>
            {
                ["http-report-status-url"] = "http://localhost/example",
                ["http-report-status-reduced-reporting"] = "true"
            });

            Assert.That(module.RequestedLogContent, Is.EqualTo(ReportLogContent.ReducedMessage));
        }

        [Test]
        public void RunScriptEnvironmentIsReduced()
        {
            var options = new Dictionary<string, string>
            {
                ["reduced-reporting"] = "true",
                ["backup-name"] = "Nightly",
                ["machine-id"] = "abc",
                ["passphrase"] = "hunter2",
                ["dbpath"] = SecretPath,
                ["auth-password"] = "secret"
            };

            var env = RunScript.BuildEnvironment("AFTER", "Backup", "s3://user:pass@bucket/folder", new[] { SecretPath }, options, "/tmp/result.txt", Library.Interface.ParsedResultType.Warning);

            Assert.That(env.Keys, Is.EquivalentTo(new[]
            {
                "DUPLICATI__reduced_reporting",
                "DUPLICATI__backup_name",
                "DUPLICATI__machine_id",
                "DUPLICATI__EVENTNAME",
                "DUPLICATI__OPERATIONNAME",
                "DUPLICATI__PARSED_RESULT",
                "DUPLICATI__RESULTFILE"
            }));
            Assert.That(string.Join("\n", env.Values), Does.Not.Contain("John Doe").And.Not.Contain("hunter2").And.Not.Contain("user:pass"));
        }

        [Test]
        public void RunScriptEnvironmentIsFullWhenNotReduced()
        {
            var options = new Dictionary<string, string> { ["passphrase"] = "hunter2" };
            var env = RunScript.BuildEnvironment("BEFORE", "Backup", "file:///target", new[] { SecretPath }, options, null, null);

            Assert.That(env["DUPLICATI__REMOTEURL"], Is.EqualTo("file:///target"));
            Assert.That(env["DUPLICATI__LOCALPATH"], Is.EqualTo(SecretPath));
            Assert.That(env["DUPLICATI__passphrase"], Is.EqualTo("hunter2"));
            Assert.That(env.ContainsKey("DUPLICATI__RESULTFILE"), Is.False);
        }

        [Test]
        public async Task StatusReportUsesTheReducedLineComputedByTheSenderAsync()
        {
            using var module = CreateStatusModule(reduced: true);
            Exception thrown;
            try { throw new System.IO.IOException($"Could not find file '{SecretPath}'"); }
            catch (Exception ex) { thrown = ex; }

            var entry = new LogEntry("Excluding {0}", new object[] { SecretPath }, LogMessageType.Warning, "Duplicati.Tag", "PermissionDenied", thrown);
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnLogEntryAsync(new ReportLogEntry(entry.AsString(true), "Warning", entry.FilterTag, entry.Id, entry.When, thrown.ToString(), null, ReducedReportFormat.FormatLogLine(entry)), CancellationToken.None);
            await module.OnOperationCompletedAsync(null!, thrown, CancellationToken.None);

            var completed = module.Reports.Last();
            Assert.That(completed.RecentLogLines.Single(), Does.Match(LogLineRule));
            Assert.That(completed.RecentLogLines.Single(), Does.Contain("[ex:System.IO.IOException] [st:Duplicati.UnitTest.ReducedReportModuleTests."));
            Assert.That(completed.ErrorMessage, Does.StartWith("System.IO.IOException [st:Duplicati.UnitTest.ReducedReportModuleTests."));
            Assert.That(JsonSerializer.Serialize(module.Reports), Does.Not.Contain("John Doe"));
        }

        [Test]
        public async Task StatusReportErrorMessageIsPathRedactedWhenNotReducedAsync()
        {
            using var module = CreateStatusModule(reduced: false);
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnOperationCompletedAsync(null!, new System.IO.IOException($"Could not find file /home/alice/secret.txt"), CancellationToken.None);

            var completed = module.Reports.Last();
            Assert.That(completed.ErrorMessage, Does.Contain("Could not find file"));
            Assert.That(completed.ErrorMessage, Does.Not.Contain("/home/alice"));
        }

        [Test]
        public void UsageReporterDescribesExceptionsWithoutMessages()
        {
            Exception captured;
            try
            {
                try { throw new System.IO.IOException($"Could not find file '{SecretPath}'"); }
                catch (Exception inner) { throw new InvalidOperationException($"Backup of {SecretPath} failed", inner); }
            }
            catch (Exception ex)
            {
                captured = ex;
            }

            var description = Reporter.DescribeException(captured);
            Assert.That(description, Does.Contain("System.InvalidOperationException"));
            Assert.That(description, Does.Contain("System.IO.IOException"));
            Assert.That(description, Does.Contain("UsageReporterDescribesExceptionsWithoutMessages"), "stack frames are kept");
            Assert.That(description, Does.Not.Contain("John Doe"));
            Assert.That(description, Does.Not.Contain("Could not find"));
            // Without file information the frames have no source file paths or line numbers
            Assert.That(description, Does.Not.Contain(".cs"));
            Assert.That(description, Does.Not.Contain(":line"));
        }

        /// <summary>
        /// An exception that reports a stack trace of its own making
        /// </summary>
        private sealed class ExceptionWithCustomStackTrace : Exception
        {
            public override string StackTrace => $"   at Custom.Frame() in {SecretPath}:line 1";
        }

        [Test]
        public void UsageReporterIgnoresStackTraceTextSuppliedByTheException()
        {
            Exception captured;
            try { throw new ExceptionWithCustomStackTrace(); }
            catch (Exception ex) { captured = ex; }

            var description = Reporter.DescribeException(captured);
            Assert.That(description, Does.Contain(nameof(ExceptionWithCustomStackTrace)));
            Assert.That(description, Does.Contain(nameof(UsageReporterIgnoresStackTraceTextSuppliedByTheException)), "the real frames are reported");
            Assert.That(description, Does.Not.Contain("John Doe"));
            Assert.That(description, Does.Not.Contain("Custom.Frame"));
        }
    }
}
