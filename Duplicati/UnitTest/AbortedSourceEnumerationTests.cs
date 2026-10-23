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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// "Stop now" while listing the source is stuck, as listing a folder or reading the attributes
/// of a file on a network share that stopped answering can be. These are synchronous calls
/// that do not look at the cancellation token, so the listing does not end when the backup
/// is stopped, and the backup waited for it for ever. The listing runs twice, once for the
/// backup and once to count the files for the progress bar; both get stuck.
/// </summary>
[NonParallelizable]
public class AbortedSourceEnumerationTests : BasicSetupHelper
{
    private static readonly ManualResetEventSlim Release = new(false);
    private static int s_stuck;
    private static string s_stuckOn = "";

    private static void GetStuck()
    {
        Interlocked.Increment(ref s_stuck);
        Release.Wait();
    }

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
            // Listing the folder is synchronous, as it is for the file system
            if (s_stuckOn == "listing" && !Root)
                GetStuck();
            foreach (var c in Children)
                yield return c;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            switch (name)
            {
                case "get_Path": return EntryPath;
                case "get_IsFolder": return Folder;
                case "get_IsRootEntry": return Root;
                case "get_Attributes":
                    // Reading the attributes is synchronous, as a stat on the file system is
                    if (s_stuckOn == "attributes" && !Folder)
                        GetStuck();
                    return Folder ? FileAttributes.Directory : FileAttributes.Normal;
                case "get_Size": return Folder ? 0L : 1L;
                case "get_CreatedUtc":
                case "get_LastModificationUtc": return new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                case "Enumerate": return EnumerateAsync((CancellationToken)args![0]!);
                case "OpenRead": return Task.FromResult<Stream>(new MemoryStream(new byte[] { 1 }));
                case "GetMinorMetadata": return Task.FromResult(new Dictionary<string, string?>());
                case "FileExists": return Task.FromResult(false);
            }
            var type = targetMethod.ReturnType;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    private sealed class StuckListingProvider : ISourceProviderModule
    {
        private readonly string _mountPoint;

        public StuckListingProvider() { _mountPoint = string.Empty; }

        public StuckListingProvider(string url, string mountPoint, Dictionary<string, string?> options) { _mountPoint = mountPoint; }

        public string Key => "test-stuck-listing";
        public string DisplayName => "Test stuck listing provider";
        public string Description => "Test provider whose listing stops answering";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public string MountedPath => _mountPoint;
        public bool NeedsStoredMetadata => false;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return Entry.Create(_mountPoint, true, true,
                Entry.Create(_mountPoint + "sub" + Path.DirectorySeparatorChar, true, false,
                    Entry.Create(_mountPoint + "sub" + Path.DirectorySeparatorChar + "file.txt", false)));
        }

        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
            => Task.FromResult<ISourceProviderEntry?>(null);

        public void Dispose() { }
    }

    [TestCase("listing")]
    [TestCase("attributes")]
    [Category("Targeted")]
    public async Task AbortedBackupWithAStuckListingReturnsAsync(string stuckOn)
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new StuckListingProvider());
        Release.Reset();
        s_stuck = 0;
        s_stuckOn = stuckOn;

        var options = new Dictionary<string, string>(TestOptions) { ["no-encryption"] = "true" };

        try
        {
            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            var backupTask = Task.Run(async () => await c.BackupAsync(["@/stuck|test-stuck-listing://source"]));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (s_stuck == 0 && sw.Elapsed < TimeSpan.FromMinutes(1) && !backupTask.IsCompleted)
                await Task.Delay(50);
            Assert.That(s_stuck, Is.GreaterThan(0), "The listing did not get stuck");
            await Task.Delay(500);

            await c.AbortAsync();

            var stopped = await Task.WhenAny(backupTask, Task.Delay(TimeSpan.FromSeconds(30))) == backupTask;
            Assert.That(stopped, Is.True, "The abort did not make the backup return within 30 seconds");
            Assert.That(async () => await backupTask, Throws.InstanceOf<OperationCanceledException>(),
                "An aborted backup should end with the cancellation");

            // The listing that was left behind ends later; that must not disturb anything
            Release.Set();
            await Task.Delay(500);

            // The local database is closed once the backup has returned
            using var fs = new FileStream(options["dbpath"], FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            s_stuckOn = "";
            Release.Set();
        }
    }
}
