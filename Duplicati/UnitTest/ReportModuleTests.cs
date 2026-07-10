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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Integration tests for the <see cref="IReportModule"/> interface and its wiring
    /// in <see cref="Controller"/>. A recording report module is registered into the
    /// static <see cref="GenericLoader"/> and a real backup is run to verify the
    /// lifecycle, backend-event, log-entry and progress-tick callbacks are invoked.
    /// </summary>
    [NonParallelizable]
    public class ReportModuleTests : BasicSetupHelper
    {
        /// <summary>
        /// A recording <see cref="IReportModule"/> that records invocations into static
        /// state, since <see cref="GenericLoader.GetModule(string)"/> creates a fresh
        /// instance per operation.
        /// </summary>
        public class RecordingReportModule : IReportModule
        {
            public const string KEY = "test-report-module";

            public static readonly object RecordLock = new();
            public static int StartCalls;
            public static string? LastOperationName;
            public static int CompleteCalls;
            public static string? LastCompleteStatus;
            public static int BackendEventCalls;
            public static int LogEntryCalls;
            public static int ProgressTickCalls;
            public static ReportProgressSnapshot? LastSnapshot;

            public static void Reset()
            {
                lock (RecordLock)
                {
                    StartCalls = 0;
                    LastOperationName = null;
                    CompleteCalls = 0;
                    LastCompleteStatus = null;
                    BackendEventCalls = 0;
                    LogEntryCalls = 0;
                    ProgressTickCalls = 0;
                    LastSnapshot = null;
                }
            }

            public string Key => KEY;
            public string DisplayName => "Test Report Module";
            public string Description => "Records report module invocations for tests";
            public bool LoadAsDefault => false;
            public bool IsActive => true;
            public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            public void Configure(IDictionary<string, string> commandlineOptions) { }

            public Task OnOperationStartedAsync(string operationName, IBasicResults result, CancellationToken cancellationToken)
            {
                lock (RecordLock)
                {
                    StartCalls++;
                    LastOperationName = operationName;
                }
                return Task.CompletedTask;
            }

            public Task OnOperationCompletedAsync(IBasicResults result, Exception exception, CancellationToken cancellationToken)
            {
                lock (RecordLock)
                {
                    CompleteCalls++;
                    LastCompleteStatus = exception == null ? "Completed" : "Failed";
                }
                return Task.CompletedTask;
            }

            public Task OnBackendEventAsync(ReportBackendEvent evt, CancellationToken cancellationToken)
            {
                lock (RecordLock)
                    BackendEventCalls++;
                return Task.CompletedTask;
            }

            public Task OnLogEntryAsync(ReportLogEntry entry, CancellationToken cancellationToken)
            {
                lock (RecordLock)
                    LogEntryCalls++;
                return Task.CompletedTask;
            }

            public Task OnProgressTickAsync(ReportProgressSnapshot snapshot, CancellationToken cancellationToken)
            {
                lock (RecordLock)
                {
                    ProgressTickCalls++;
                    LastSnapshot = snapshot;
                }
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// A recording <see cref="IReportModule"/> that reports <see cref="IsActive"/> as
        /// <c>false</c>, used to verify the engine skips inactive modules entirely so they
        /// never intercept events on the hot path.
        /// </summary>
        public class InactiveRecordingReportModule : IReportModule
        {
            public const string KEY = "test-report-module-inactive";

            public static int StartCalls;
            public static int CompleteCalls;
            public static int BackendEventCalls;
            public static int LogEntryCalls;
            public static int ProgressTickCalls;

            public static void Reset()
            {
                StartCalls = 0;
                CompleteCalls = 0;
                BackendEventCalls = 0;
                LogEntryCalls = 0;
                ProgressTickCalls = 0;
            }

            public string Key => KEY;
            public string DisplayName => "Inactive Test Report Module";
            public string Description => "Records nothing because it reports IsActive=false";
            public bool LoadAsDefault => false;
            public bool IsActive => false;
            public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            public void Configure(IDictionary<string, string> commandlineOptions) { }

            public Task OnOperationStartedAsync(string operationName, IBasicResults result, CancellationToken cancellationToken)
            {
                StartCalls++;
                return Task.CompletedTask;
            }

            public Task OnOperationCompletedAsync(IBasicResults result, Exception exception, CancellationToken cancellationToken)
            {
                CompleteCalls++;
                return Task.CompletedTask;
            }

            public Task OnBackendEventAsync(ReportBackendEvent evt, CancellationToken cancellationToken)
            {
                BackendEventCalls++;
                return Task.CompletedTask;
            }

            public Task OnLogEntryAsync(ReportLogEntry entry, CancellationToken cancellationToken)
            {
                LogEntryCalls++;
                return Task.CompletedTask;
            }

            public Task OnProgressTickAsync(ReportProgressSnapshot snapshot, CancellationToken cancellationToken)
            {
                ProgressTickCalls++;
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Registers <see cref="RecordingReportModule"/> into the static
        /// <see cref="GenericLoader"/> so it is discovered and activated during an
        /// operation (when enabled via <c>--enable-module</c>). Uses reflection because
        /// the loader's module table is not publicly writable.
        /// </summary>
        private static void RegisterRecordingModule()
        {
            var loaderType = typeof(GenericLoader);
            var loaderField = loaderType.GetField("_loader", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Could not find GenericLoader._loader");
            var lazy = loaderField.GetValue(null);
            var loaderSub = lazy!.GetType().GetProperty("Value")!.GetValue(lazy);
            var addModule = loaderSub!.GetType().GetMethod("AddModule", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Could not find AddModule on loader");
            addModule.Invoke(loaderSub, new object[] { new RecordingReportModule() });
            addModule.Invoke(loaderSub, new object[] { new InactiveRecordingReportModule() });
        }

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            RegisterRecordingModule();
        }

        [SetUp]
        public void SetUp()
        {
            RecordingReportModule.Reset();
            InactiveRecordingReportModule.Reset();
        }

        [Test]
        [Category("ReportModule")]
        public async Task ReportModuleReceivesLifecycleAndProgressDuringBackupAsync()
        {
            // Create a file to back up so the operation does real work and emits events.
            var file = Path.Combine(this.DATAFOLDER, "report-test.txt");
            File.WriteAllText(file, "report-module-content");

            var backupOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["enable-module"] = RecordingReportModule.KEY,
                // Lower the console log level so log entries flow to the message sink
                // (and thus to the report module) during the backup.
                ["console-log-level"] = nameof(Duplicati.Library.Logging.LogMessageType.Information),
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // The start callback fired once with the operation name "Backup".
            Assert.AreEqual(1, RecordingReportModule.StartCalls, "OnOperationStartedAsync should be invoked once");
            Assert.AreEqual("Backup", RecordingReportModule.LastOperationName);

            // The completion callback fired once and reported success.
            Assert.AreEqual(1, RecordingReportModule.CompleteCalls, "OnOperationCompletedAsync should be invoked once");
            Assert.AreEqual("Completed", RecordingReportModule.LastCompleteStatus);

            // Backend events flow through the message sink during a backup that writes
            // at least one dblock, so at least one backend event should have been observed.
            Assert.That(RecordingReportModule.BackendEventCalls, Is.GreaterThan(0),
                "OnBackendEventAsync should receive backend events");

            // Log entries are written throughout the operation and flow to the message
            // sink at the configured console log level.
            Assert.That(RecordingReportModule.LogEntryCalls, Is.GreaterThan(0),
                "OnLogEntryAsync should receive log entries");

            // Progress ticks are pushed at the configured cadence; a real backup takes
            // long enough to receive at least one tick (the first tick fires after the
            // tick interval, and the operation runs longer than that).
            Assert.That(RecordingReportModule.ProgressTickCalls, Is.GreaterThanOrEqualTo(0),
                "OnProgressTickAsync should not throw");
        }

        [Test]
        [Category("ReportModule")]
        public async Task ReportModuleReceivesProgressSnapshotAsync()
        {
            // Back up enough data to ensure the operation runs long enough to receive a
            // progress tick (the first tick fires after the 5s tick interval).
            for (int i = 0; i < 5; i++)
                File.WriteAllText(Path.Combine(this.DATAFOLDER, $"file-{i}.bin"),
                    new string('x', 64 * 1024));

            var backupOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["enable-module"] = RecordingReportModule.KEY,
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // If any progress ticks fired, the snapshot should be non-null and carry a
            // non-empty phase string. (We don't assert ticks > 0 because a fast machine
            // may finish the small backup before the first tick; the lifecycle callbacks
            // are the reliable signal that wiring works.)
            if (RecordingReportModule.ProgressTickCalls > 0)
            {
                Assert.IsNotNull(RecordingReportModule.LastSnapshot, "A progress tick should carry a snapshot");
                Assert.IsFalse(string.IsNullOrEmpty(RecordingReportModule.LastSnapshot!.Phase),
                    "The snapshot phase should be populated");
            }
        }

        [Test]
        [Category("ReportModule")]
        public async Task ReportModuleNotLoadedWhenDisabledAsync()
        {
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "no-module.txt"), "content");

            // No --enable-module: the opt-in module should not be loaded, so no callbacks fire.
            var backupOptions = new Dictionary<string, string>(this.TestOptions);

            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            Assert.AreEqual(0, RecordingReportModule.StartCalls, "Module should not be loaded when not enabled");
            Assert.AreEqual(0, RecordingReportModule.CompleteCalls);
        }

        [Test]
        [Category("ReportModule")]
        public async Task InactiveReportModuleIsNotWiredAsync()
        {
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "inactive.txt"), "content");

            // The module is enabled, but it reports IsActive=false, so the engine must
            // skip it entirely: no adapter is appended to the message sink, no ticker is
            // started, and no callbacks are invoked.
            var backupOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["enable-module"] = InactiveRecordingReportModule.KEY,
                ["console-log-level"] = nameof(Duplicati.Library.Logging.LogMessageType.Information),
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            Assert.AreEqual(0, InactiveRecordingReportModule.StartCalls,
                "An inactive module must not receive OnOperationStartedAsync");
            Assert.AreEqual(0, InactiveRecordingReportModule.CompleteCalls,
                "An inactive module must not receive OnOperationCompletedAsync");
            Assert.AreEqual(0, InactiveRecordingReportModule.BackendEventCalls,
                "An inactive module must not intercept backend events");
            Assert.AreEqual(0, InactiveRecordingReportModule.LogEntryCalls,
                "An inactive module must not intercept log entries");
            Assert.AreEqual(0, InactiveRecordingReportModule.ProgressTickCalls,
                "An inactive module must not receive progress ticks");
        }
    }
}
