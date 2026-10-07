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
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A source that stops answering while the backup reads the size or the metadata of an entry,
/// as a network share can. These are synchronous calls that do not look at the cancellation
/// token, so they do not end when the backup is stopped, and the backup waited for them.
/// </summary>
[NonParallelizable]
public class AbortedSourceMetadataTests : BasicSetupHelper
{
    /// <summary>
    /// Where the source gets stuck
    /// </summary>
    public enum StuckAt
    {
        /// <summary>Reading the size of the file</summary>
        Size,
        /// <summary>Reading the extended attributes of the file for its metadata</summary>
        FileMetadata,
        /// <summary>Reading the extended attributes of the folder for its metadata</summary>
        FolderMetadata,
        /// <summary>Reading the size of the file for the progress count only</summary>
        CountedSize,
    }

    private static readonly ManualResetEventSlim Release = new(false);
    private static StuckAt s_stuckAt;
    private static int s_stuck;

    /// <summary>
    /// Blocks the calling thread until the test releases it
    /// </summary>
    private static void Stall()
    {
        Interlocked.Increment(ref s_stuck);
        Release.Wait();
    }

    /// <summary>
    /// Checks if the current call comes from a given class, so the source gets stuck only in the
    /// stage under test and not in an earlier one that reads the same value
    /// </summary>
    private static bool CalledFrom(string className)
        => new StackTrace().ToString().Contains("." + className + ".", StringComparison.Ordinal);

    /// <summary>
    /// A source entry, made as a proxy so it does not name the members of the interface
    /// </summary>
    public class Entry : DispatchProxy
    {
        public string EntryPath = "";
        public bool Folder;
        public bool Root;
        public List<ISourceProviderEntry> Children = new();

        public static ISourceProviderEntry Create(string path, bool folder, bool root = false, params ISourceProviderEntry[] children)
        {
            var proxy = Create<ISourceProviderEntry, Entry>();
            var e = (Entry)(object)proxy;
            e.EntryPath = path;
            e.Folder = folder;
            e.Root = root;
            e.Children.AddRange(children);
            return proxy;
        }

        private async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken token)
        {
            await Task.CompletedTask;
            foreach (var c in Children)
                yield return c;
        }

        private long GetSize()
        {
            if (!Folder && (s_stuckAt == StuckAt.Size || (s_stuckAt == StuckAt.CountedSize && CalledFrom("CountFilesHandler"))))
                Stall();
            return Folder ? 0L : 1L;
        }

        // Reading the metadata is synchronous, as it is for a file: the task is only returned
        // once the attributes are read
        private Task<Dictionary<string, string?>> GetMinorMetadata()
        {
            var stuck = Folder ? s_stuckAt == StuckAt.FolderMetadata : s_stuckAt == StuckAt.FileMetadata;
            if (stuck && CalledFrom("MetadataGenerator"))
                Stall();
            return Task.FromResult(new Dictionary<string, string?>());
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_Path": return EntryPath;
                case "get_IsFolder": return Folder;
                case "get_IsRootEntry": return Root;
                case "get_Attributes": return Folder ? FileAttributes.Directory : FileAttributes.Normal;
                case "get_Size": return GetSize();
                case "get_CreatedUtc":
                case "get_LastModificationUtc": return new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                case "Enumerate": return EnumerateAsync((CancellationToken)args![0]!);
                case "OpenRead": return Task.FromResult<Stream>(new MemoryStream(new byte[] { 1 }));
                case "GetMinorMetadata": return GetMinorMetadata();
                case "FileExists": return Task.FromResult(false);
            }
            var type = targetMethod.ReturnType;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    private sealed class StuckMetadataProvider : ISourceProviderModule
    {
        private readonly string _mountPoint;

        public StuckMetadataProvider() { _mountPoint = string.Empty; }

        public StuckMetadataProvider(string url, string mountPoint, Dictionary<string, string?> options) { _mountPoint = mountPoint; }

        public string Key => "test-stuck-metadata";
        public string DisplayName => "Test stuck metadata provider";
        public string Description => "Test provider whose size or metadata cannot be read";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public string MountedPath => _mountPoint;
        public bool NeedsStoredMetadata => false;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return Entry.Create(_mountPoint, true, true, Entry.Create(_mountPoint + "file.txt", false));
        }

        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
            => Task.FromResult<ISourceProviderEntry?>(null);

        public void Dispose() { }
    }

    private const string Source = "@/stuck|test-stuck-metadata://source";

    private Dictionary<string, string> Prepare(StuckAt stuckAt, bool countFiles)
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new StuckMetadataProvider());
        Release.Reset();
        s_stuckAt = stuckAt;
        s_stuck = 0;

        return new Dictionary<string, string>(TestOptions)
        {
            ["no-encryption"] = "true",
            ["disable-file-scanner"] = countFiles ? "false" : "true",
        };
    }

    private static async Task WaitUntilStuckAsync(Task backupTask)
    {
        var sw = Stopwatch.StartNew();
        while (s_stuck == 0 && sw.Elapsed < TimeSpan.FromMinutes(1) && !backupTask.IsCompleted)
            await Task.Delay(50);
        Assert.That(s_stuck, Is.GreaterThan(0), "The source did not get stuck");
        await Task.Delay(500);
    }

    [Test]
    [Category("Targeted")]
    [TestCase(StuckAt.Size, false)]
    [TestCase(StuckAt.Size, true)]
    [TestCase(StuckAt.FileMetadata, false)]
    [TestCase(StuckAt.FolderMetadata, false)]
    public async Task AbortedBackupWithAStuckSourceReturnsAsync(StuckAt stuckAt, bool countFiles)
    {
        var options = Prepare(stuckAt, countFiles);
        try
        {
            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            var backupTask = Task.Run(async () => await c.BackupAsync([Source]));
            await WaitUntilStuckAsync(backupTask);

            await c.AbortAsync();

            var stopped = await Task.WhenAny(backupTask, Task.Delay(TimeSpan.FromSeconds(30))) == backupTask;
            Assert.That(stopped, Is.True, "The abort did not make the backup return within 30 seconds");
            Assert.That(async () => await backupTask, Throws.InstanceOf<OperationCanceledException>(),
                "An aborted backup should end with the cancellation");

            // The local database is closed once the backup has returned
            using var fs = new FileStream(options["dbpath"], FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            Release.Set();
        }
    }

    /// <summary>
    /// The progress count reads the size of every file on the side. When that read is stuck,
    /// the backup itself can finish, and it then waited for the count for ever.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task BackupFinishesWhileTheProgressCountIsStuckAsync()
    {
        var options = Prepare(StuckAt.CountedSize, true);
        try
        {
            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            var backupTask = Task.Run(async () => await c.BackupAsync([Source]));
            await WaitUntilStuckAsync(backupTask);

            var finished = await Task.WhenAny(backupTask, Task.Delay(TimeSpan.FromSeconds(30))) == backupTask;
            Assert.That(finished, Is.True, "The backup did not finish within 30 seconds while the progress count was stuck");
            TestUtils.AssertResults(await backupTask);
        }
        finally
        {
            Release.Set();
        }
    }
}
