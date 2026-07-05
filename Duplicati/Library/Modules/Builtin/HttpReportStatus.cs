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
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Uri = System.Uri;

namespace Duplicati.Library.Modules.Builtin
{
    /// <summary>
    /// A reporting module that periodically posts the current operation status to a
    /// remote URL as JSON. While an operation is running the engine pushes progress
    /// snapshots at a fixed cadence (see <see cref="IReportModule.OnProgressTickAsync"/>);
    /// this module posts an update at most once every <c>--http-report-status-interval</c>
    /// (default 30 seconds), including the latest progress snapshot, the counts of
    /// backend events and log entries observed so far, and the last few log lines.
    /// </summary>
    public class HttpReportStatus : IReportModule, IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<HttpReportStatus>();

        #region Option names

        /// <summary>
        /// Option used to specify the remote URL to post status reports to.
        /// </summary>
        private const string OPTION_URL = "http-report-status-url";

        /// <summary>
        /// Option used to specify the interval between status reports.
        /// </summary>
        private const string OPTION_INTERVAL = "http-report-status-interval";

        /// <summary>
        /// Option used to specify the maximum number of recent log lines included in each report.
        /// </summary>
        private const string OPTION_MAX_LOG_LINES = "http-report-status-max-log-lines";

        /// <summary>
        /// Option used to accept any SSL certificate.
        /// </summary>
        private const string OPTION_ACCEPT_ANY_CERTIFICATE = "http-report-status-accept-any-ssl-certificate";

        /// <summary>
        /// Option used to accept a specific SSL certificate hash.
        /// </summary>
        private const string OPTION_ACCEPT_SPECIFIED_CERTIFICATE = "http-report-status-accept-specified-ssl-hash";

        /// <summary>
        /// Option used to ignore certificate revocation check failures.
        /// </summary>
        private const string OPTION_IGNORE_REVOCATION_FAILURE = "http-report-status-ignore-revocation-failure";

        /// <summary>
        /// The module-specific option used to disable path redaction in the buffered log
        /// lines. Mirrors the global <c>allow-paths-in-log-messages</c> option used by the
        /// reporting helpers.
        /// </summary>
        private const string OPTION_ALLOW_PATHS_IN_LOG_MESSAGES = "http-report-status-allow-paths-in-log-messages";

        /// <summary>
        /// The global option used to disable path redaction in log messages, mirrored
        /// from <c>Options.cs</c> / <c>ReportHelper</c>. The module-specific option takes
        /// precedence, falling back to this global setting when not set.
        /// </summary>
        private const string OPTION_GLOBAL_ALLOW_PATHS_IN_LOG_MESSAGES = "allow-paths-in-log-messages";

        #endregion

        #region Defaults

        /// <summary>
        /// The default interval between status reports.
        /// </summary>
        private const string DEFAULT_INTERVAL = "30s";

        /// <summary>
        /// The default maximum number of recent log lines to include.
        /// </summary>
        private const int DEFAULT_MAX_LOG_LINES = 20;

        #endregion

        /// <summary>
        /// The module key, used to activate or deactivate the module on the commandline.
        /// </summary>
        public string Key => "httpreportstatus";

        /// <summary>
        /// A localized string describing the module with a friendly name.
        /// </summary>
        public string DisplayName => Strings.HttpReportStatus.DisplayName;

        /// <summary>
        /// A localized description of the module.
        /// </summary>
        public string Description => Strings.HttpReportStatus.Description;

        /// <summary>
        /// The module is loaded, but inactive unless a url has been set
        /// </summary>
        public bool LoadAsDefault => true;

        /// <summary>
        /// Gets a list of supported commandline arguments.
        /// </summary>
        public IList<ICommandLineArgument> SupportedCommands =>
        [
            new CommandLineArgument(OPTION_URL, CommandLineArgument.ArgumentType.String, Strings.HttpReportStatus.UrlShort, Strings.HttpReportStatus.UrlLong),
            new CommandLineArgument(OPTION_INTERVAL, CommandLineArgument.ArgumentType.Timespan, Strings.HttpReportStatus.IntervalShort, Strings.HttpReportStatus.IntervalLong, DEFAULT_INTERVAL),
            new CommandLineArgument(OPTION_MAX_LOG_LINES, CommandLineArgument.ArgumentType.Integer, Strings.HttpReportStatus.MaxLogLinesShort, Strings.HttpReportStatus.MaxLogLinesLong, DEFAULT_MAX_LOG_LINES.ToString()),
            new CommandLineArgument(OPTION_ACCEPT_ANY_CERTIFICATE, CommandLineArgument.ArgumentType.Boolean, Strings.HttpReportStatus.AcceptAnyCertificateShort, Strings.HttpReportStatus.AcceptAnyCertificateLong),
            new CommandLineArgument(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, CommandLineArgument.ArgumentType.String, Strings.HttpReportStatus.AcceptSpecifiedCertificateShort, Strings.HttpReportStatus.AcceptSpecifiedCertificateLong),
            new CommandLineArgument(OPTION_IGNORE_REVOCATION_FAILURE, CommandLineArgument.ArgumentType.Boolean, Strings.HttpReportStatus.IgnoreRevocationFailureShort, Strings.HttpReportStatus.IgnoreRevocationFailureLong, "false"),
            new CommandLineArgument(OPTION_ALLOW_PATHS_IN_LOG_MESSAGES, CommandLineArgument.ArgumentType.Boolean, Strings.HttpReportStatus.AllowPathsInLogMessagesShort, Strings.HttpReportStatus.AllowPathsInLogMessagesLong, "false"),
        ];

        /// <summary>
        /// The remote URL to post status reports to.
        /// </summary>
        private string m_url;

        /// <summary>
        /// The HTTP handler created during <see cref="Configure"/>, reused for every post
        /// and disposed when the module is disposed.
        /// </summary>
        private HttpClientHandler m_httpHandler;

        /// <summary>
        /// The interval between status reports.
        /// </summary>
        private TimeSpan m_interval;

        /// <summary>
        /// The maximum number of recent log lines to include in each report.
        /// </summary>
        private int m_maxLogLines;

        /// <summary>
        /// True if paths are allowed in the buffered log lines (i.e. not redacted).
        /// </summary>
        private bool m_allowPathsInLogMessages;

        /// <summary>
        /// The operation name reported in <see cref="OnOperationStartedAsync"/>.
        /// </summary>
        private string m_operationName;

        /// <summary>
        /// The time the operation started, in UTC.
        /// </summary>
        private DateTime m_operationStarted;

        /// <summary>
        /// The number of backend events observed so far.
        /// </summary>
        private int m_backendEvents;

        /// <summary>
        /// The number of log entries observed so far.
        /// </summary>
        private int m_logEntries;

        /// <summary>
        /// The most recent progress snapshot observed.
        /// </summary>
        private ReportProgressSnapshot m_latestSnapshot;

        /// <summary>
        /// A ring buffer of the most recent log lines observed.
        /// </summary>
        private readonly LinkedList<string> m_recentLogLines = new();

        /// <summary>
        /// The time the next status report may be sent.
        /// </summary>
        private DateTime m_nextReportTime;

        /// <summary>
        /// Lock protecting the mutable counters and buffers.
        /// </summary>
        private readonly object m_lock = new();

        /// <summary>
        /// JSON serializer options that emit property names in camelCase.
        /// </summary>
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Configures the module from the commandline options. The module is only
        /// active when <c>--http-report-status-url</c> is set.
        /// </summary>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati.</param>
        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            commandlineOptions.TryGetValue(OPTION_URL, out m_url);
            if (string.IsNullOrWhiteSpace(m_url))
            {
                // Without a URL there is nothing to report to; leave the module inactive.
                m_url = null;
                return;
            }

            m_interval = Utility.Utility.ParseTimespanOption(commandlineOptions.AsReadOnly(), OPTION_INTERVAL, DEFAULT_INTERVAL);
            if (m_interval <= TimeSpan.Zero)
                m_interval = TimeSpan.FromSeconds(30);

            m_maxLogLines = Utility.Utility.ParseIntOption(commandlineOptions.AsReadOnly(), OPTION_MAX_LOG_LINES, DEFAULT_MAX_LOG_LINES);
            if (m_maxLogLines < 0)
                m_maxLogLines = DEFAULT_MAX_LOG_LINES;

            var acceptAnyCertificate = Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), OPTION_ACCEPT_ANY_CERTIFICATE);
            var acceptSpecificCertificates = commandlineOptions.ContainsKey(OPTION_ACCEPT_SPECIFIED_CERTIFICATE)
                ? commandlineOptions[OPTION_ACCEPT_SPECIFIED_CERTIFICATE].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                : null;
            var ignoreRevocationFailure = Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), OPTION_IGNORE_REVOCATION_FAILURE);

            m_httpHandler = new HttpClientHandler();
            HttpClientHelper.ConfigureHandlerCertificateValidator(m_httpHandler, acceptAnyCertificate, acceptSpecificCertificates, ignoreRevocationFailure);

            // Paths in log lines are redacted by default. The module-specific option takes
            // precedence and falls back to the global allow-paths-in-log-messages setting,
            // mirroring the behavior of the other reporting modules (see ReportHelper).
            if (commandlineOptions.TryGetValue(OPTION_ALLOW_PATHS_IN_LOG_MESSAGES, out var allowPathsModule) && bool.TryParse(allowPathsModule, out var parsedModule))
                m_allowPathsInLogMessages = parsedModule;
            else
                m_allowPathsInLogMessages = Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), OPTION_GLOBAL_ALLOW_PATHS_IN_LOG_MESSAGES);
        }

        /// <inheritdoc />
        public Task OnOperationStartedAsync(string operationName, IBasicResults result, CancellationToken cancellationToken)
        {
            if (!IsActive)
                return Task.CompletedTask;

            lock (m_lock)
            {
                m_operationName = operationName ?? "Operation";
                m_operationStarted = DateTime.UtcNow;
                m_backendEvents = 0;
                m_logEntries = 0;
                m_latestSnapshot = null;
                m_recentLogLines.Clear();
                m_nextReportTime = DateTime.UtcNow;
            }

            return PostAsync(BuildReport("Started", null), cancellationToken);
        }

        /// <inheritdoc />
        public async Task OnOperationCompletedAsync(IBasicResults result, Exception exception, CancellationToken cancellationToken)
        {
            if (!IsActive)
                return;

            // Refresh the snapshot with the final result so the completed report is accurate.
            lock (m_lock)
            {
                m_latestSnapshot = new ReportProgressSnapshot(
                    "Complete",
                    1f,
                    0, 0, 0, 0, false,
                    null, 0, 0,
                    Array.Empty<ReportBackendEvent>());
            }

            var report = BuildReport(exception == null ? "Completed" : "Failed", exception?.Message);
            await PostAsync(report, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task OnBackendEventAsync(ReportBackendEvent evt, CancellationToken cancellationToken)
        {
            if (!IsActive)
                return Task.CompletedTask;

            lock (m_lock)
                m_backendEvents++;

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnLogEntryAsync(ReportLogEntry entry, CancellationToken cancellationToken)
        {
            if (!IsActive || entry == null)
                return Task.CompletedTask;

            lock (m_lock)
            {
                m_logEntries++;
                // Redact paths in the buffered log line unless the user has explicitly
                // allowed paths in log messages, mirroring the reporting helpers.
                var line = m_allowPathsInLogMessages ? entry.Message : SensitiveDataFilter.RedactPaths(entry.Message);
                m_recentLogLines.AddLast(line);
                while (m_recentLogLines.Count > m_maxLogLines)
                    m_recentLogLines.RemoveFirst();
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnProgressTickAsync(ReportProgressSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (!IsActive)
                return Task.CompletedTask;

            bool shouldReport;
            lock (m_lock)
            {
                m_latestSnapshot = snapshot;
                shouldReport = DateTime.UtcNow >= m_nextReportTime;
                if (shouldReport)
                    m_nextReportTime = DateTime.UtcNow + m_interval;
            }

            if (!shouldReport)
                return Task.CompletedTask;

            return PostAsync(BuildReport("Progress", null), cancellationToken);
        }

        /// <summary>
        /// Gets a value indicating whether the module is configured with a target URL.
        /// </summary>
        /// <inheritdoc />
        /// <remarks>The module is only active when a target URL has been configured,
        /// so that no per-event interception happens unless there is somewhere to
        /// report to.</remarks>
        public bool IsActive => !string.IsNullOrWhiteSpace(m_url);

        /// <summary>
        /// Builds the JSON-serializable status report from the current state.
        /// </summary>
        /// <param name="status">The status label for this report (e.g. <c>Started</c>, <c>Progress</c>, <c>Completed</c>).</param>
        /// <param name="errorMessage">An error message to include, or <c>null</c>.</param>
        /// <returns>A status report ready to be serialized and posted.</returns>
        private StatusReport BuildReport(string status, string errorMessage)
        {
            lock (m_lock)
            {
                return new StatusReport
                {
                    Operation = m_operationName,
                    Status = status,
                    StartedUtc = m_operationStarted,
                    ReportedUtc = DateTime.UtcNow,
                    ErrorMessage = errorMessage,
                    BackendEvents = m_backendEvents,
                    LogEntries = m_logEntries,
                    Progress = m_latestSnapshot == null ? null : new ProgressSnapshot(m_latestSnapshot),
                    RecentLogLines = [.. m_recentLogLines],
                };
            }
        }

        /// <summary>
        /// Posts the given status report to the configured URL as JSON. Failures are
        /// logged but never propagated to the caller.
        /// </summary>
        /// <param name="report">The report to post.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task PostAsync(StatusReport report, CancellationToken cancellationToken)
        {
            if (report == null)
                return;

            try
            {
                var json = JsonSerializer.Serialize(report, typeof(StatusReport), SerializerOptions);
                await SendAsync(json, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "HttpReportStatusSendError", ex, "Failed to post status report to {0}: {1}", m_url, ex.Message);
            }
        }

        /// <summary>
        /// Sends the JSON body to the configured URL. This is virtual so tests can
        /// intercept the request without performing real network I/O.
        /// </summary>
        /// <param name="json">The JSON body to send.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        protected virtual async Task SendAsync(string json, CancellationToken cancellationToken)
        {
            using var client = HttpClientHelper.CreateClient(m_httpHandler);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(new Uri(m_url), content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Disposes the handler created during <see cref="Configure"/>.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> when disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            try { m_httpHandler?.Dispose(); }
            catch { }
            m_httpHandler = null;
        }

        /// <summary>
        /// The JSON status report posted to the remote URL.
        /// </summary>
        public sealed class StatusReport
        {
            /// <summary>The operation name.</summary>
            public string Operation { get; set; }

            /// <summary>The status label for this report.</summary>
            public string Status { get; set; }

            /// <summary>The time the operation started, in UTC.</summary>
            public DateTime StartedUtc { get; set; }

            /// <summary>The time this report was generated, in UTC.</summary>
            public DateTime ReportedUtc { get; set; }

            /// <summary>An error message, if the operation failed.</summary>
            public string ErrorMessage { get; set; }

            /// <summary>The number of backend events observed so far.</summary>
            public int BackendEvents { get; set; }

            /// <summary>The number of log entries observed so far.</summary>
            public int LogEntries { get; set; }

            /// <summary>The latest progress snapshot, or <c>null</c>.</summary>
            public ProgressSnapshot Progress { get; set; }

            /// <summary>The most recent log lines observed.</summary>
            public List<string> RecentLogLines { get; set; }
        }

        /// <summary>
        /// The progress portion of the status report.
        /// </summary>
        public sealed class ProgressSnapshot
        {
            /// <summary>Parameterless constructor for JSON deserialization.</summary>
            public ProgressSnapshot() { }

            /// <summary>Creates a progress snapshot from the interface record.</summary>
            public ProgressSnapshot(ReportProgressSnapshot snapshot)
            {
                Phase = snapshot.Phase;
                Progress = snapshot.Progress;
                FilesProcessed = snapshot.FilesProcessed;
                FileSizeProcessed = snapshot.FileSizeProcessed;
                FileCount = snapshot.FileCount;
                FileSize = snapshot.FileSize;
                CountingFiles = snapshot.CountingFiles;
                CurrentFilename = snapshot.CurrentFilename;
                CurrentFileOffset = snapshot.CurrentFileOffset;
                ActiveTransfers = snapshot.ActiveTransfers?.Length ?? 0;
            }

            /// <summary>The current operation phase name.</summary>
            public string Phase { get; set; }

            /// <summary>The overall progress, in the range [0, 1].</summary>
            public float Progress { get; set; }

            /// <summary>The number of files processed so far.</summary>
            public long FilesProcessed { get; set; }

            /// <summary>The number of bytes processed so far.</summary>
            public long FileSizeProcessed { get; set; }

            /// <summary>The total number of files.</summary>
            public long FileCount { get; set; }

            /// <summary>The total number of bytes.</summary>
            public long FileSize { get; set; }

            /// <summary>True if the file count and size are not yet final.</summary>
            public bool CountingFiles { get; set; }

            /// <summary>The file currently being processed, or null.</summary>
            public string CurrentFilename { get; set; }

            /// <summary>The byte offset reached in the file currently being processed.</summary>
            public long CurrentFileOffset { get; set; }

            /// <summary>The number of active backend transfers.</summary>
            public int ActiveTransfers { get; set; }
        }
    }
}
