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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation;
using Duplicati.Library.Main.Operation.Restore;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the <see cref="IRestoreCallbackModule"/> restore hook interface and its
    /// wiring in <see cref="RestoreHandler"/> / the restore FileProcessor.
    /// </summary>
    [NonParallelizable]
    public class RestoreCallbackModuleTests : BasicSetupHelper
    {
        /// <summary>
        /// A minimal <see cref="IRestoreCallbackModule"/> implementation that records calls
        /// on the instance, used for the dispatch-helper unit tests.
        /// </summary>
        private sealed class FakeRestoreCallbackModule : IRestoreCallbackModule, IGenericPriorityModule
        {
            public string Key { get; set; } = "fake";
            public string DisplayName => "Fake";
            public string Description => "Fake restore callback module";
            public bool LoadAsDefault => false;
            public int Priority { get; set; }
            public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            public List<(string files, long version, DateTime timestamp)> PrepareCalls { get; } = new();
            public int BulkStartCalls { get; set; }
            public List<(long version, string path, DateTime timestamp)> FileRestoredCalls { get; } = new();

            public Action<IList<string>>? OnPrepare { get; set; }
            public Action? OnBulk { get; set; }
            public Action<long, string>? OnFile { get; set; }

            public Task OnPreparePriorityFilesAsync(IList<string> priorityFiles, long version, DateTime backupTimestamp, CancellationToken cancellationToken)
            {
                PrepareCalls.Add((string.Join(",", priorityFiles), version, backupTimestamp));
                OnPrepare?.Invoke(priorityFiles);
                return Task.CompletedTask;
            }
            public Task OnBulkRestoreStartAsync(CancellationToken cancellationToken)
            {
                BulkStartCalls++;
                OnBulk?.Invoke();
                return Task.CompletedTask;
            }
            public Task OnFileRestoredAsync(long version, string path, DateTime backupTimestamp, CancellationToken cancellationToken)
            {
                FileRestoredCalls.Add((version, path, backupTimestamp));
                OnFile?.Invoke(version, path);
                return Task.CompletedTask;
            }

            public void Configure(IDictionary<string, string> commandlineOptions) { }
        }

        /// <summary>
        /// A module that is NOT a restore callback module, used to verify the dispatch
        /// helpers skip non-matching modules.
        /// </summary>
        private sealed class NonCallbackModule : IGenericModule
        {
            public string Key => "non-callback";
            public string DisplayName => "NonCallback";
            public string Description => "";
            public bool LoadAsDefault => false;
            public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();
            public void Configure(IDictionary<string, string> commandlineOptions) { }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task InvokePreparePriorityFilesCallsModulesAndAllowsMutationAsync()
        {
            var modA = new FakeRestoreCallbackModule { Key = "a" };
            var modB = new FakeRestoreCallbackModule { Key = "b" };
            modA.OnPrepare = list => list.Add("from-a");
            modB.OnPrepare = list => list.Remove("remove-me");

            var priorityFiles = new List<string> { "keep", "remove-me" };
            // Mix in a non-callback module to ensure it is skipped.
            const long expectedVersion = 7;
            var expectedTimestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            await RestoreHandler.InvokePreparePriorityFilesAsync(new IGenericModule[] { new NonCallbackModule(), modA, modB }, priorityFiles, expectedVersion, expectedTimestamp, CancellationToken.None);

            // Both callback modules were invoked exactly once, in order.
            Assert.AreEqual(1, modA.PrepareCalls.Count);
            Assert.AreEqual(1, modB.PrepareCalls.Count);
            // The version and backup timestamp were forwarded to the modules.
            Assert.AreEqual(expectedVersion, modA.PrepareCalls[0].version);
            Assert.AreEqual(expectedTimestamp, modA.PrepareCalls[0].timestamp);
            Assert.AreEqual(expectedVersion, modB.PrepareCalls[0].version);
            Assert.AreEqual(expectedTimestamp, modB.PrepareCalls[0].timestamp);
            // modA saw the original list, modB saw the list after modA mutated it.
            Assert.AreEqual("keep,remove-me", modA.PrepareCalls[0].files);
            Assert.AreEqual("keep,remove-me,from-a", modB.PrepareCalls[0].files);

            // The shared list reflects both mutations.
            Assert.That(priorityFiles, Is.EqualTo(new[] { "keep", "from-a" }));
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task InvokePreparePriorityFilesIsolatedFromThrowingModulesAsync()
        {
            var throwing = new FakeRestoreCallbackModule { Key = "throw", OnPrepare = _ => throw new InvalidOperationException("boom") };
            var after = new FakeRestoreCallbackModule { Key = "after" };

            var priorityFiles = new List<string> { "a" };
            await RestoreHandler.InvokePreparePriorityFilesAsync(new IGenericModule[] { throwing, after }, priorityFiles, 0, DateTime.MinValue, CancellationToken.None);

            // The throwing module did not abort dispatch to the next module.
            Assert.AreEqual(1, after.PrepareCalls.Count);
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task InvokeBulkRestoreStartCallsAllModulesInOrderAsync()
        {
            var order = new List<string>();
            var modA = new FakeRestoreCallbackModule { Key = "a", OnBulk = () => order.Add("a") };
            var modB = new FakeRestoreCallbackModule { Key = "b", OnBulk = () => order.Add("b") };

            await RestoreHandler.InvokeBulkRestoreStartAsync(new IGenericModule[] { new NonCallbackModule(), modA, modB }, CancellationToken.None);

            Assert.AreEqual(1, modA.BulkStartCalls);
            Assert.AreEqual(1, modB.BulkStartCalls);
            Assert.That(order, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task InvokeFileRestoredPassesVersionAndPathAsync()
        {
            var mod = new FakeRestoreCallbackModule();
            const long expectedVersion = 2;
            var expectedTimestamp = new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            await RestoreHandler.InvokeFileRestoredAsync(new IGenericModule[] { new NonCallbackModule(), mod }, expectedVersion, "/some/path/file.txt", expectedTimestamp, CancellationToken.None);

            Assert.AreEqual(1, mod.FileRestoredCalls.Count);
            Assert.AreEqual(expectedVersion, mod.FileRestoredCalls[0].version);
            Assert.AreEqual("/some/path/file.txt", mod.FileRestoredCalls[0].path);
            Assert.AreEqual(expectedTimestamp, mod.FileRestoredCalls[0].timestamp);
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task InvokeFileRestoredIsolatedFromThrowingModulesAsync()
        {
            var throwing = new FakeRestoreCallbackModule { Key = "throw", OnFile = (_, _) => throw new InvalidOperationException("boom") };
            var after = new FakeRestoreCallbackModule { Key = "after" };

            await RestoreHandler.InvokeFileRestoredAsync(new IGenericModule[] { throwing, after }, 0, "/p", DateTime.MinValue, CancellationToken.None);

            Assert.AreEqual(1, after.FileRestoredCalls.Count);
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task DispatchHelpersAcceptNullModulesAsync()
        {
            // Passing null must not throw.
            await RestoreHandler.InvokePreparePriorityFilesAsync(null, new List<string>(), 0, DateTime.MinValue, CancellationToken.None);
            await RestoreHandler.InvokeBulkRestoreStartAsync(null, CancellationToken.None);
            await RestoreHandler.InvokeFileRestoredAsync(null, 0, "/p", DateTime.MinValue, CancellationToken.None);
            Assert.Pass();
        }

        // ----------------------------------------------------------------------------------
        // Integration tests: register a real module in the GenericLoader and run a restore.
        // ----------------------------------------------------------------------------------

        /// <summary>
        /// A restore-callback module that records invocations into static state. Static
        /// state is required because <see cref="GenericLoader.GetModule(string)"/> creates a
        /// fresh instance per operation, so the instance that runs during the restore is not
        /// the same instance the test registered.
        /// </summary>
        public class RecordingRestoreCallbackModule : IRestoreCallbackModule
        {
            public const string KEY = "test-restore-callback";

            public static readonly object RecordLock = new();
            public static List<(string files, long version, DateTime timestamp)> PrepareCalls { get; } = new();
            public static int BulkStartCalls;
            public static List<(long version, string path, DateTime timestamp)> FileRestoredCalls { get; } = new();

            public static void Reset()
            {
                lock (RecordLock)
                {
                    PrepareCalls.Clear();
                    BulkStartCalls = 0;
                    FileRestoredCalls.Clear();
                }
            }

            public string Key => KEY;
            public string DisplayName => "Test Restore Callback";
            public string Description => "Records restore callback invocations for tests";
            public bool LoadAsDefault => false;
            public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            public void Configure(IDictionary<string, string> commandlineOptions) { }

            public Task OnPreparePriorityFilesAsync(IList<string> priorityFiles, long version, DateTime backupTimestamp, CancellationToken cancellationToken)
            {
                lock (RecordLock)
                    PrepareCalls.Add((string.Join("|", priorityFiles), version, backupTimestamp));
                return Task.CompletedTask;
            }

            public Task OnBulkRestoreStartAsync(CancellationToken cancellationToken)
            {
                lock (RecordLock)
                    BulkStartCalls++;
                return Task.CompletedTask;
            }

            public Task OnFileRestoredAsync(long version, string path, DateTime backupTimestamp, CancellationToken cancellationToken)
            {
                lock (RecordLock)
                    FileRestoredCalls.Add((version, path, backupTimestamp));
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Registers <see cref="RecordingRestoreCallbackModule"/> into the static
        /// <see cref="GenericLoader"/> so it is discovered and activated during a restore
        /// operation (when enabled via <c>--enable-module</c>). Uses reflection because the
        /// loader's module table is not publicly writable.
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
            addModule.Invoke(loaderSub, new object[] { new RecordingRestoreCallbackModule() });
        }

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            RegisterRecordingModule();
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreCallbacksFireOnNewRestoreAsync()
        {
            RecordingRestoreCallbackModule.Reset();

            // Write two files, one of which we mark as a priority file via the restore
            // destination provider (priority files are derived from the target folder).
            var file1 = Path.Combine(this.DATAFOLDER, "file1.txt");
            var file2 = Path.Combine(this.DATAFOLDER, "file2.txt");
            File.WriteAllText(file1, "content-1");
            File.WriteAllText(file2, "content-2");

            var backupOptions = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-legacy"] = "false",
                ["enable-module"] = RecordingRestoreCallbackModule.KEY,
            };

            var restoreStartedAt = DateTime.UtcNow;

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            // OnPreparePriorityFilesAsync is always called (even with an empty list).
            Assert.That(RecordingRestoreCallbackModule.PrepareCalls.Count, Is.GreaterThanOrEqualTo(1), "OnPreparePriorityFilesAsync should be invoked");
            // The reported version is 0 (newest backup) for the single restored version.
            Assert.AreEqual(0, RecordingRestoreCallbackModule.PrepareCalls[0].version, "Prepare should report version 0");
            // The reported backup timestamp is the newest backup's timestamp, created just before this restore.
            Assert.That(RecordingRestoreCallbackModule.PrepareCalls[0].timestamp, Is.GreaterThan(restoreStartedAt.AddMinutes(-5)).And.LessThan(DateTime.UtcNow.AddMinutes(1)),
                "Prepare should report a recent backup timestamp");
            // OnBulkRestoreStartAsync is called exactly once when the bulk restore starts.
            Assert.AreEqual(1, RecordingRestoreCallbackModule.BulkStartCalls, "OnBulkRestoreStartAsync should be invoked once");
            // OnFileRestoredAsync is called for each restored file on the new engine.
            Assert.That(RecordingRestoreCallbackModule.FileRestoredCalls.Count, Is.GreaterThanOrEqualTo(2), "OnFileRestoredAsync should be invoked for each restored file");
            // The reported version is 0 (newest backup) for the single restored version.
            Assert.IsTrue(RecordingRestoreCallbackModule.FileRestoredCalls.All(x => x.version == 0), "Restored files should report version 0");
            // The reported backup timestamp is recent (the backup was created just before this restore).
            Assert.IsTrue(RecordingRestoreCallbackModule.FileRestoredCalls.All(x => x.timestamp > restoreStartedAt.AddMinutes(-5) && x.timestamp < DateTime.UtcNow.AddMinutes(1)),
                "Restored files should report a recent backup timestamp");
            // The reported paths are within the restore folder.
            Assert.IsTrue(RecordingRestoreCallbackModule.FileRestoredCalls.All(x => x.path.StartsWith(this.RESTOREFOLDER, StringComparison.OrdinalIgnoreCase)),
                "Restored file paths should be under the restore folder");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task OnFileRestoredNotCalledOnLegacyRestoreAsync()
        {
            RecordingRestoreCallbackModule.Reset();

            var file1 = Path.Combine(this.DATAFOLDER, "legacy-file.txt");
            File.WriteAllText(file1, "legacy-content");

            var backupOptions = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-legacy"] = "true",
                ["enable-module"] = RecordingRestoreCallbackModule.KEY,
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            // OnPreparePriorityFilesAsync and OnBulkRestoreStartAsync are invoked on the legacy engine too.
            Assert.That(RecordingRestoreCallbackModule.PrepareCalls.Count, Is.GreaterThanOrEqualTo(1), "OnPreparePriorityFilesAsync should be invoked on legacy restore");
            Assert.AreEqual(1, RecordingRestoreCallbackModule.BulkStartCalls, "OnBulkRestoreStartAsync should be invoked once on legacy restore");
            // OnFileRestoredAsync must NOT be called when --restore-legacy is set.
            Assert.AreEqual(0, RecordingRestoreCallbackModule.FileRestoredCalls.Count, "OnFileRestoredAsync must not be called on legacy restore");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreCallbackReceivesEmptyPriorityListWhenNoPriorityFilesAsync()
        {
            RecordingRestoreCallbackModule.Reset();

            var file1 = Path.Combine(this.DATAFOLDER, "nopriority.txt");
            File.WriteAllText(file1, "no-priority-content");

            var backupOptions = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-legacy"] = "false",
                ["enable-module"] = RecordingRestoreCallbackModule.KEY,
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            // With no priority files, OnPreparePriorityFilesAsync is still called once with an empty list,
            // and OnBulkRestoreStartAsync is called exactly once (the bulk restore starts immediately).
            Assert.AreEqual(1, RecordingRestoreCallbackModule.PrepareCalls.Count, "OnPreparePriorityFilesAsync should be invoked exactly once");
            Assert.AreEqual(string.Empty, RecordingRestoreCallbackModule.PrepareCalls[0].files, "Priority list should be empty");
            Assert.AreEqual(1, RecordingRestoreCallbackModule.BulkStartCalls, "OnBulkRestoreStartAsync should be invoked once");
            Assert.That(RecordingRestoreCallbackModule.FileRestoredCalls.Count, Is.GreaterThanOrEqualTo(1));
        }
    }
}
