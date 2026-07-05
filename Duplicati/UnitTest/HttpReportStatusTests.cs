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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Unit tests for the <see cref="HttpReportStatus"/> reporting module.
    /// These tests exercise the module's logic directly (no network I/O) by
    /// overriding <see cref="HttpReportStatus.SendAsync"/> to capture the
    /// posted JSON payloads.
    /// </summary>
    [Category("ReportModule")]
    public class HttpReportStatusTests
    {
        /// <summary>
        /// A subclass of <see cref="HttpReportStatus"/> that records every JSON
        /// payload posted via <see cref="HttpReportStatus.SendAsync"/> instead of
        /// performing real network I/O.
        /// </summary>
        private sealed class CapturingHttpReportStatus : HttpReportStatus
        {
            public List<string> PostedJsons { get; } = new();
            public List<HttpReportStatus.StatusReport> Reports { get; } = new();

            private static readonly JsonSerializerOptions DeserializerOptions = new()
            {
                PropertyNameCaseInsensitive = true,
            };

            protected override Task SendAsync(string json, CancellationToken cancellationToken)
            {
                PostedJsons.Add(json);
                Reports.Add(JsonSerializer.Deserialize<HttpReportStatus.StatusReport>(json, DeserializerOptions)!);
                return Task.CompletedTask;
            }
        }

        private static CapturingHttpReportStatus CreateConfigured(string interval = "1s", int maxLogLines = 20)
        {
            var module = new CapturingHttpReportStatus();
            module.Configure(new Dictionary<string, string>
            {
                ["http-report-status-url"] = "http://localhost/example",
                ["http-report-status-interval"] = interval,
                ["http-report-status-max-log-lines"] = maxLogLines.ToString(),
            });
            return module;
        }

        private static ReportProgressSnapshot SampleSnapshot(string phase = "Backup_ProcessingFiles", float progress = 0.5f)
            => new ReportProgressSnapshot(
                phase, progress,
                3, 100, 10, 1000,
                false,
                "/some/file.txt", 100, 50,
                Array.Empty<ReportBackendEvent>());

        [Test]
        public async Task InactiveWithoutUrlAsync()
        {
            var module = new CapturingHttpReportStatus();
            module.Configure(new Dictionary<string, string>());
            // An unconfigured module reports itself as inactive, so the engine skips it.
            Assert.IsFalse(module.IsActive, "Module should be inactive without a URL");
            // All callbacks should be no-ops without a configured URL.
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnBackendEventAsync(new ReportBackendEvent("Put", "Started", "/p", 1), CancellationToken.None);
            await module.OnLogEntryAsync(new ReportLogEntry("hi", "Information", "tag", "id", DateTime.UtcNow, null), CancellationToken.None);
            await module.OnProgressTickAsync(SampleSnapshot(), CancellationToken.None);
            await module.OnOperationCompletedAsync(null!, null, CancellationToken.None);
            Assert.AreEqual(0, module.PostedJsons.Count);
        }

        [Test]
        public void IsActiveReflectsConfiguration()
        {
            var unconfigured = new HttpReportStatus();
            unconfigured.Configure(new Dictionary<string, string>());
            Assert.IsFalse(unconfigured.IsActive);

            using var configured = new HttpReportStatus();
            configured.Configure(new Dictionary<string, string>
            {
                ["http-report-status-url"] = "http://localhost/example",
            });
            Assert.IsTrue(configured.IsActive);
        }

        [Test]
        public async Task StartedAndCompletedArePostedAsync()
        {
            using var module = CreateConfigured();
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnOperationCompletedAsync(null!, null, CancellationToken.None);

            Assert.AreEqual(2, module.Reports.Count);
            Assert.AreEqual("Started", module.Reports[0].Status);
            Assert.AreEqual("Backup", module.Reports[0].Operation);
            Assert.AreEqual("Completed", module.Reports[1].Status);
            // The completed report carries a final progress snapshot at 100%.
            Assert.IsNotNull(module.Reports[1].Progress);
            Assert.AreEqual(1f, module.Reports[1].Progress!.Progress);
            // The Started report is not completed, the Completed report is.
            Assert.IsFalse(module.Reports[0].IsCompleted, "Started report should not be completed");
            Assert.IsTrue(module.Reports[1].IsCompleted, "Completed report should be marked completed");
            // The JSON is serialized with camelCase property names.
            Assert.That(module.PostedJsons[0], Does.Contain("\"operation\""), "JSON keys should be camelCase");
            Assert.That(module.PostedJsons[0], Does.Contain("\"startedUtc\""));
            Assert.That(module.PostedJsons[0], Does.Not.Contain("\"StartedUtc\""));
        }

        [Test]
        public async Task FailedReportIsCompletedAsync()
        {
            using var module = CreateConfigured();
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnOperationCompletedAsync(null!, new InvalidOperationException("boom"), CancellationToken.None);

            Assert.AreEqual("Failed", module.Reports[1].Status);
            Assert.IsTrue(module.Reports[1].IsCompleted, "Failed report should be marked completed");
        }

        [Test]
        public async Task CompletedCarriesFailureMessageAsync()
        {
            using var module = CreateConfigured();
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnOperationCompletedAsync(null!, new InvalidOperationException("boom"), CancellationToken.None);

            Assert.AreEqual("Failed", module.Reports[1].Status);
            Assert.AreEqual("boom", module.Reports[1].ErrorMessage);
        }

        [Test]
        public async Task BackendEventsAndLogEntriesAreCountedAsync()
        {
            using var module = CreateConfigured();
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnBackendEventAsync(new ReportBackendEvent("Put", "Started", "/a", 1), CancellationToken.None);
            await module.OnBackendEventAsync(new ReportBackendEvent("Put", "Completed", "/a", 1), CancellationToken.None);
            await module.OnLogEntryAsync(new ReportLogEntry("line1", "Information", "t", "i", DateTime.UtcNow, null), CancellationToken.None);
            await module.OnLogEntryAsync(new ReportLogEntry("line2", "Warning", "t", "i", DateTime.UtcNow, null), CancellationToken.None);
            await module.OnProgressTickAsync(SampleSnapshot(), CancellationToken.None);

            // The progress tick should have produced one Progress report (interval is 1s).
            var progressReport = module.Reports.Find(r => r.Status == "Progress");
            Assert.IsNotNull(progressReport, "A progress report should be posted on the first tick");
            Assert.AreEqual(2, progressReport!.BackendEvents, "Two backend events were observed");
            Assert.AreEqual(2, progressReport.LogEntries, "Two log entries were observed");
            Assert.AreEqual(2, progressReport.RecentLogLines.Count, "Both log lines are buffered");
            Assert.AreEqual("line1", progressReport.RecentLogLines[0]);
            Assert.AreEqual("line2", progressReport.RecentLogLines[1]);
        }

        [Test]
        public async Task ProgressSnapshotIsForwardedAsync()
        {
            using var module = CreateConfigured();
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            var snapshot = SampleSnapshot("Backup_ProcessingFiles", 0.25f);
            await module.OnProgressTickAsync(snapshot, CancellationToken.None);

            var progressReport = module.Reports.Find(r => r.Status == "Progress")!;
            Assert.IsNotNull(progressReport.Progress);
            Assert.AreEqual("Backup_ProcessingFiles", progressReport.Progress!.Phase);
            Assert.AreEqual(0.25f, progressReport.Progress.Progress);
            Assert.AreEqual(3, progressReport.Progress.FilesProcessed);
            Assert.AreEqual("/some/file.txt", progressReport.Progress.CurrentFilename);
        }

        [Test]
        public async Task ProgressReportsAreThrottledByIntervalAsync()
        {
            var module = CreateConfigured(interval: "1h"); // very long interval => only the first tick posts
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnProgressTickAsync(SampleSnapshot(), CancellationToken.None); // posts
            await module.OnProgressTickAsync(SampleSnapshot(), CancellationToken.None); // throttled
            await module.OnProgressTickAsync(SampleSnapshot(), CancellationToken.None); // throttled

            var progressReports = module.Reports.FindAll(r => r.Status == "Progress");
            Assert.AreEqual(1, progressReports.Count, "Only the first tick should post within the long interval");
        }

        [Test]
        public async Task RecentLogLinesAreCappedAsync()
        {
            var module = CreateConfigured(maxLogLines: 2);
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnLogEntryAsync(new ReportLogEntry("a", "Information", "t", "i", DateTime.UtcNow, null), CancellationToken.None);
            await module.OnLogEntryAsync(new ReportLogEntry("b", "Information", "t", "i", DateTime.UtcNow, null), CancellationToken.None);
            await module.OnLogEntryAsync(new ReportLogEntry("c", "Information", "t", "i", DateTime.UtcNow, null), CancellationToken.None);
            await module.OnProgressTickAsync(SampleSnapshot(), CancellationToken.None);

            var progressReport = module.Reports.Find(r => r.Status == "Progress")!;
            Assert.AreEqual(2, progressReport.RecentLogLines.Count, "Only the last 2 log lines are kept");
            Assert.AreEqual("b", progressReport.RecentLogLines[0]);
            Assert.AreEqual("c", progressReport.RecentLogLines[1]);
        }

        [Test]
        public void SupportedCommandsIncludeUrlAndInterval()
        {
            var module = new HttpReportStatus();
            var keys = new HashSet<string>();
            foreach (var cmd in module.SupportedCommands)
                keys.Add(cmd.Name);

            Assert.IsTrue(keys.Contains("http-report-status-url"));
            Assert.IsTrue(keys.Contains("http-report-status-interval"));
            Assert.IsTrue(keys.Contains("http-report-status-max-log-lines"));
            Assert.IsTrue(keys.Contains("http-report-status-allow-paths-in-log-messages"));
        }

        [Test]
        public void ModuleMetadata()
        {
            var module = new HttpReportStatus();
            Assert.AreEqual("httpreportstatus", module.Key);
            // The module is loaded by default but stays inactive (see IsActive) until a URL
            // is configured, so there is no overhead unless it actually has something to do.
            Assert.IsTrue(module.LoadAsDefault, "The module should load by default");
        }

        [Test]
        public async Task ReportIncludesEnvironmentMetadataAsync()
        {
            using var module = CreateConfigured();

            // The remote URL is captured via the IGenericCallbackModule.OnStart hook, mirroring
            // ReportHelper. Drive it to populate the backup id and destination type.
            var remoteUrl = "s3://mybucket/path";
            var localPaths = new[] { "/data/source" };
            module.OnStart("Backup", ref remoteUrl, ref localPaths);
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);

            var report = module.Reports[0];
            Assert.IsNotNull(report.Metadata, "Report should include environment metadata");
            var metadata = report.Metadata!;
            Assert.IsFalse(string.IsNullOrEmpty(metadata.DuplicatiVersion), "DuplicatiVersion should be populated");
            Assert.IsFalse(string.IsNullOrEmpty(metadata.MachineId), "MachineId should be populated");
            Assert.IsFalse(string.IsNullOrEmpty(metadata.BackupId), "BackupId should be populated from the remote URL");
            Assert.IsFalse(string.IsNullOrEmpty(metadata.BackupName), "BackupName should be populated");
            Assert.IsFalse(string.IsNullOrEmpty(metadata.MachineName), "MachineName should be populated");
            Assert.AreEqual("s3", metadata.DestinationType, "DestinationType should be the remote URL scheme");
            Assert.IsFalse(string.IsNullOrEmpty(metadata.OperatingSystem), "OperatingSystem should be populated");
            Assert.IsFalse(string.IsNullOrEmpty(metadata.OperatingSystemDetailed), "OperatingSystemDetailed should be populated");
            Assert.IsFalse(string.IsNullOrEmpty(metadata.InstallationType), "InstallationType should be populated");

            // The backup id is a stable hex string derived from the remote URL.
            Assert.That(metadata.BackupId, Does.Match("^[0-9a-fA-F]+$"));

            // An s3 URL against a known public-cloud host yields a safe host suffix; an
            // unknown host yields null. The helper is shared with ReportHelper.
            Assert.IsNull(metadata.DestinationHostSuffix, "DestinationHostSuffix should be null for an unknown host");
        }

        [Test]
        public async Task DestinationHostSuffixKnownCloudAsync()
        {
            using var module = CreateConfigured();

            // An s3 URL against a recognized public-cloud host suffix yields that suffix.
            var remoteUrl = "s3://mybucket.s3.amazonaws.com/path";
            var localPaths = new[] { "/data/source" };
            module.OnStart("Backup", ref remoteUrl, ref localPaths);
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);

            var metadata = module.Reports[0].Metadata!;
            Assert.AreEqual(".amazonaws.com", metadata.DestinationHostSuffix,
                "DestinationHostSuffix should be the matched safe cloud suffix");
        }

        [Test]
        public async Task MetadataHonorsOptionOverridesAsync()
        {
            // MachineId, BackupId, BackupName and MachineName honor explicit option values
            // before falling back to the computed defaults, mirroring ReportHelper.
            var module = new CapturingHttpReportStatus();
            module.Configure(new Dictionary<string, string>
            {
                ["http-report-status-url"] = "http://localhost/example",
                ["http-report-status-interval"] = "1s",
                ["machine-id"] = "custom-machine-id",
                ["backup-id"] = "custom-backup-id",
                ["backup-name"] = "custom-backup-name",
                ["machine-name"] = "custom-machine-name",
            });

            var remoteUrl = "s3://mybucket/path";
            var localPaths = new[] { "/data/source" };
            module.OnStart("Backup", ref remoteUrl, ref localPaths);
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);

            var metadata = module.Reports[0].Metadata!;
            Assert.AreEqual("custom-machine-id", metadata.MachineId, "MachineId should use the option override");
            Assert.AreEqual("custom-backup-id", metadata.BackupId, "BackupId should use the option override");
            Assert.AreEqual("custom-backup-name", metadata.BackupName, "BackupName should use the option override");
            Assert.AreEqual("custom-machine-name", metadata.MachineName, "MachineName should use the option override");
        }

        [Test]
        public async Task PathsInLogLinesAreRedactedByDefaultAsync()
        {
            using var module = CreateConfigured();
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            await module.OnLogEntryAsync(
                new ReportLogEntry("Opening /Users/me/secret/file.txt", "Information", "t", "i", DateTime.UtcNow, null),
                CancellationToken.None);
            await module.OnProgressTickAsync(SampleSnapshot(), CancellationToken.None);

            var progressReport = module.Reports.Find(r => r.Status == "Progress")!;
            Assert.AreEqual(1, progressReport.RecentLogLines.Count);
            Assert.That(progressReport.RecentLogLines[0], Does.Contain("-redacted-"),
                "Paths should be redacted from log lines by default");
            Assert.That(progressReport.RecentLogLines[0], Does.Not.Contain("/Users/me/secret/file.txt"),
                "The original path must not appear in the buffered log line");
        }

        [Test]
        public async Task PathsInLogLinesKeptWhenAllowedByModuleOptionAsync()
        {
            using var module = new CapturingHttpReportStatus();
            module.Configure(new Dictionary<string, string>
            {
                ["http-report-status-url"] = "http://localhost/example",
                ["http-report-status-interval"] = "1s",
                ["http-report-status-allow-paths-in-log-messages"] = "true",
            });
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            var path = OperatingSystem.IsWindows() ? @"C:\Users\me\secret\file.txt" : "/Users/me/secret/file.txt";
            await module.OnLogEntryAsync(
                new ReportLogEntry("Opening " + path, "Information", "t", "i", DateTime.UtcNow, null),
                CancellationToken.None);
            await module.OnProgressTickAsync(SampleSnapshot(), CancellationToken.None);

            var progressReport = module.Reports.Find(r => r.Status == "Progress")!;
            Assert.AreEqual(1, progressReport.RecentLogLines.Count);
            Assert.That(progressReport.RecentLogLines[0], Does.Contain(path),
                "The original path should be kept when the module option is enabled");
        }

        [Test]
        public async Task PathsInLogLinesKeptWhenAllowedByGlobalOptionAsync()
        {
            using var module = new CapturingHttpReportStatus();
            module.Configure(new Dictionary<string, string>
            {
                ["http-report-status-url"] = "http://localhost/example",
                ["http-report-status-interval"] = "1s",
                ["allow-paths-in-log-messages"] = "true",
            });
            await module.OnOperationStartedAsync("Backup", null!, CancellationToken.None);
            var path = OperatingSystem.IsWindows() ? @"C:\Users\me\secret\file.txt" : "/Users/me/secret/file.txt";
            await module.OnLogEntryAsync(
                new ReportLogEntry("Opening " + path, "Information", "t", "i", DateTime.UtcNow, null),
                CancellationToken.None);
            await module.OnProgressTickAsync(SampleSnapshot(), CancellationToken.None);

            var progressReport = module.Reports.Find(r => r.Status == "Progress")!;
            Assert.AreEqual(1, progressReport.RecentLogLines.Count);
            Assert.That(progressReport.RecentLogLines[0], Does.Contain(path),
                "The original path should be kept when the global option is enabled and the module option is not set");
        }
    }
}
