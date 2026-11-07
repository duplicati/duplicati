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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The restore tells its destination whether the restore completed, so a
    /// destination that replaces something as a whole (such as a disk image) does
    /// not do so for a restore that was stopped or where files failed.
    /// </summary>
    public class RestoreDestinationCompletionTests : BasicSetupHelper
    {
        /// <summary>
        /// The key of the recording restore destination.
        /// </summary>
        private const string ProviderKey = "test-completion-destination";

        /// <summary>
        /// The names of the backed-up files.
        /// </summary>
        private static readonly string[] FileNames = Enumerable.Range(0, 10).Select(i => $"file{i}").ToArray();

        /// <summary>
        /// A restore destination that restores to a local folder and records
        /// what the restore tells it on Finalize.
        /// </summary>
        private sealed class RecordingRestoreDestinationProvider : IRestoreDestinationProviderModule
        {
            /// <summary>
            /// The folder the provider restores to.
            /// </summary>
            public static string TargetFolder { get; set; } = string.Empty;

            /// <summary>
            /// The file name that fails to open for writing, if any.
            /// </summary>
            public static string? FailingFileName { get; set; }

            /// <summary>
            /// The controller to stop when the first file is opened for writing, if any.
            /// </summary>
            public static Controller? StopOnFirstWrite { get; set; }

            /// <summary>
            /// The completion flag of each Finalize call.
            /// </summary>
            public static ConcurrentQueue<bool> FinalizeCalls { get; } = new();

            private readonly IRestoreDestinationProvider? _inner;
            private int _writes;

            /// <summary>
            /// Constructor used by the dynamic loader for module metadata.
            /// </summary>
            public RecordingRestoreDestinationProvider()
            {
            }

            /// <summary>
            /// Constructor used when instantiating the provider for an operation.
            /// </summary>
            public RecordingRestoreDestinationProvider(string url, Dictionary<string, string> options)
                => _inner = new Library.SourceProvider.FileRestoreDestinationProvider(TargetFolder, true, true);

            public string Key => ProviderKey;
            public string DisplayName => "Recording restore destination";
            public string Description => "Test provider that records the completion of the restore";
            public IList<ICommandLineArgument> SupportedCommands => [];

            private IRestoreDestinationProvider Inner
                => _inner ?? throw new InvalidOperationException("The module metadata instance does not perform restore operations");

            public string TargetDestination => Inner.TargetDestination;
            public Task Initialize(CancellationToken cancel) => Inner.Initialize(cancel);

            public Task Finalize(Action<double>? progressCallback, bool isComplete, CancellationToken cancel)
            {
                FinalizeCalls.Enqueue(isComplete);
                return Inner.Finalize(progressCallback, isComplete, cancel);
            }

            public Task Test(CancellationToken cancellationToken) => Inner.Test(cancellationToken);
            public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel) => Inner.CreateFolderIfNotExists(path, cancel);
            public Task<bool> FileExists(string path, CancellationToken cancel) => Inner.FileExists(path, cancel);

            public async Task<Stream> OpenWrite(string path, CancellationToken cancel)
            {
                await BeforeWriteAsync(path);
                return await Inner.OpenWrite(path, cancel);
            }

            public Task<Stream> OpenRead(string path, CancellationToken cancel) => Inner.OpenRead(path, cancel);

            public async Task<Stream> OpenReadWrite(string path, CancellationToken cancel)
            {
                await BeforeWriteAsync(path);
                return await Inner.OpenReadWrite(path, cancel);
            }

            /// <summary>
            /// Fails the failing file, and stops the restore when the first
            /// file is opened for writing.
            /// </summary>
            /// <param name="path">The path of the file being opened.</param>
            private async Task BeforeWriteAsync(string path)
            {
                if (FailingFileName != null && Path.GetFileName(path) == FailingFileName)
                    throw new IOException($"Test failure for {path}");

                if (Interlocked.Increment(ref _writes) == 1 && StopOnFirstWrite != null)
                    await StopOnFirstWrite.StopAsync();
            }
            public Task<long> GetFileLength(string path, CancellationToken cancel) => Inner.GetFileLength(path, cancel);
            public Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancel) => Inner.HasReadOnlyAttribute(path, cancel);
            public Task ClearReadOnlyAttribute(string path, CancellationToken cancel) => Inner.ClearReadOnlyAttribute(path, cancel);
            public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, bool hasRestoredEntriesBelow, CancellationToken cancel) => Inner.WriteMetadata(path, metadata, restoreSymlinkMetadata, restorePermissions, hasRestoredEntriesBelow, cancel);
            public Task DeleteFolder(string path, CancellationToken cancel) => Inner.DeleteFolder(path, cancel);
            public Task DeleteFile(string path, CancellationToken cancel) => Inner.DeleteFile(path, cancel);
            public IList<string> GetPriorityFiles() => Inner.GetPriorityFiles();
            public void Dispose() => _inner?.Dispose();
        }

        [SetUp]
        public void SetUpProvider()
        {
            Library.DynamicLoader.RestoreDestinationProviderLoader.AddSourceProvider(new RecordingRestoreDestinationProvider());
            RecordingRestoreDestinationProvider.TargetFolder = RESTOREFOLDER;
            RecordingRestoreDestinationProvider.FailingFileName = null;
            RecordingRestoreDestinationProvider.StopOnFirstWrite = null;
            RecordingRestoreDestinationProvider.FinalizeCalls.Clear();
        }

        [TearDown]
        public void TearDownProvider()
        {
            RecordingRestoreDestinationProvider.FailingFileName = null;
            RecordingRestoreDestinationProvider.StopOnFirstWrite = null;
        }

        /// <summary>
        /// Writes the source files, each in its own volume, and backs them up.
        /// </summary>
        private async Task BackupAsync()
        {
            var rng = new Random(42);
            foreach (var name in FileNames)
            {
                var data = new byte[64 * 1024];
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, name), data);
            }

            using var c = new Controller("file://" + TARGETFOLDER, BackupOptions(), null);
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        }

        private Dictionary<string, string> BackupOptions()
            => new(TestOptions) { ["blocksize"] = "16kb", ["dblock-size"] = "64kb" };

        /// <summary>
        /// The restore runs one file processor, so a stop requested while the
        /// first file is written leaves files that are not restored.
        /// </summary>
        private Dictionary<string, string> RestoreOptions()
            => new(BackupOptions())
            {
                ["restore-path"] = $"@{ProviderKey}://target",
                ["restore-legacy"] = "false",
                ["restore-file-processors"] = "1",
                ["restore-with-local-blocks"] = "false",
            };

        /// <summary>
        /// Runs the restore, which may fail.
        /// </summary>
        /// <param name="controller">The controller to run the restore with.</param>
        /// <returns>The restore results, or null if the restore failed.</returns>
        private static async Task<RestoreResults?> TryRestoreAsync(Controller controller)
        {
            try
            {
                return (RestoreResults)await controller.RestoreAsync(["*"]);
            }
            catch (Exception ex) when (ex is not AssertionException)
            {
                TestContext.Progress.WriteLine($"The restore failed: {ex.Message}");
                return null;
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task Restore_Complete_FinalizesAsComplete()
        {
            await BackupAsync();

            using (var c = new Controller("file://" + TARGETFOLDER, RestoreOptions(), null))
                TestUtils.AssertResults(await c.RestoreAsync(["*"]));

            CollectionAssert.AreEqual(new[] { true }, RecordingRestoreDestinationProvider.FinalizeCalls, "A complete restore is finalized as complete.");
            foreach (var name in FileNames)
                Assert.IsTrue(File.Exists(Path.Combine(RESTOREFOLDER, name)), $"{name} should be restored.");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task Restore_FailedFile_FinalizesAsIncomplete()
        {
            await BackupAsync();
            RecordingRestoreDestinationProvider.FailingFileName = FileNames[3];

            RestoreResults? results;
            using (var c = new Controller("file://" + TARGETFOLDER, RestoreOptions(), null))
                results = await TryRestoreAsync(c);

            Assert.IsNotNull(results, "A failing file is skipped, not a failed restore.");
            Assert.IsTrue(results!.BrokenLocalFiles.Any(f => Path.GetFileName(f) == FileNames[3]), "The failing file is reported as not restored.");
            CollectionAssert.AreEqual(new[] { false }, RecordingRestoreDestinationProvider.FinalizeCalls, "A restore with a failed file is finalized as incomplete.");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task Restore_Stopped_FinalizesAsIncomplete()
        {
            await BackupAsync();

            using (var c = new Controller("file://" + TARGETFOLDER, RestoreOptions(), null))
            {
                RecordingRestoreDestinationProvider.StopOnFirstWrite = c;
                await TryRestoreAsync(c);
            }

            CollectionAssert.DoesNotContain(RecordingRestoreDestinationProvider.FinalizeCalls, true, "A stopped restore is never finalized as complete.");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task Restore_MissingVolume_IsNotFinalizedAsComplete()
        {
            await BackupAsync();

            var volume = Directory.GetFiles(TARGETFOLDER, "*.dblock.*").OrderBy(f => f, StringComparer.Ordinal).First();
            File.Delete(volume);

            var options = RestoreOptions();
            options["no-backend-verification"] = "true";
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                await TryRestoreAsync(c);

            CollectionAssert.DoesNotContain(RecordingRestoreDestinationProvider.FinalizeCalls, true, "A restore with a missing volume is never finalized as complete.");
        }
    }
}
