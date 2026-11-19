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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A source whose entries are streams of unknown length that can only be read once and in
/// order, as the piped output of a dump tool is. The backup must read them one at a time in
/// the order they are listed, record the size that was read, and keep the copy of the previous
/// backup when a stream fails part-way.
/// </summary>
[NonParallelizable]
public class StreamedSourceTests : BasicSetupHelper
{
    /// <summary>A file of the test source</summary>
    /// <param name="Name">The file name</param>
    /// <param name="Data">The content</param>
    /// <param name="FailAfter">The number of bytes after which the stream fails, or a negative number to not fail</param>
    private sealed record TestFile(string Name, byte[] Data, int FailAfter);

    private static List<TestFile> Files = [];
    private static readonly List<string> OpenOrder = [];
    private static DateTime RunTime;
    private static int OpenStreams;
    private static int MaxOpenStreams;

    private sealed class TestStream(TestFile file) : Stream, IStreamedSourceStream
    {
        private int m_position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (file.FailAfter >= 0 && m_position >= file.FailAfter)
                throw new IOException("The test stream failed");

            var limit = file.FailAfter >= 0 ? Math.Min(file.FailAfter, file.Data.Length) : file.Data.Length;
            var n = Math.Min(count, limit - m_position);
            Array.Copy(file.Data, m_position, buffer, offset, n);
            m_position += n;
            return n;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Interlocked.Decrement(ref OpenStreams);
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => m_position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TestEntry(string path, TestFile? file) : ISequentialSourceProviderEntry
    {
        public bool IsFolder => file == null;
        public bool IsMetaEntry => false;
        public bool IsRootEntry => file == null;
        public DateTime CreatedUtc => RunTime;
        public DateTime LastModificationUtc => RunTime;
        public string Path => path;
        public long Size => IsFolder ? 0 : -1;
        public bool IsSymlink => false;
        public string? SymlinkTarget => null;
        public FileAttributes Attributes => IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
        public bool IsSpecialFile => false;
        public bool IsCharacterDevice => false;
        public bool IsAlternateStream => false;
        public string? HardlinkTargetId => null;

        public Task<Stream> OpenRead(CancellationToken cancellationToken)
        {
            lock (OpenOrder)
            {
                OpenOrder.Add(file!.Name);
                MaxOpenStreams = Math.Max(MaxOpenStreams, Interlocked.Increment(ref OpenStreams));
            }

            return Task.FromResult<Stream>(new TestStream(file));
        }

        public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>());

        public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            foreach (var f in Files)
                yield return new TestEntry(path + f.Name, f);
        }
    }

    private sealed class TestProvider : ISourceProviderModule, ISequentialSourceProvider
    {
        private readonly string m_mountPoint;

        public TestProvider()
        {
            m_mountPoint = string.Empty;
        }

        public TestProvider(string url, string mountPoint, Dictionary<string, string?> options)
        {
            m_mountPoint = mountPoint;
        }

        public string Key => "test-streamed";
        public string DisplayName => "Test streamed provider";
        public string Description => "Test provider whose files are streams of unknown length";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public string MountedPath => m_mountPoint;
        public bool NeedsStoredMetadata => false;
        public bool NeedsCaseSensitiveFilters => false;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new TestEntry(Library.Common.IO.Util.AppendDirSeparator(m_mountPoint), null);
        }

        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
            => Task.FromResult<ISourceProviderEntry?>(null);

        public void Dispose() { }
    }

    private const string Source = "@/streamed|test-streamed://source";

    private static byte[] Data(int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    private Dictionary<string, string> Options => new(TestOptions)
    {
        ["no-encryption"] = "true",
        ["blocksize"] = "10kb",
        ["concurrency-fileprocessors"] = "4",
    };

    private async Task<IBackupResults> BackupAsync(List<TestFile> files, DateTime runTime)
    {
        Files = files;
        RunTime = runTime;
        OpenOrder.Clear();
        OpenStreams = 0;
        MaxOpenStreams = 0;

        using var c = new Controller("file://" + TARGETFOLDER, Options, null);
        return await c.BackupAsync([Source]);
    }

    private async Task<Dictionary<string, long>> ListLatestAsync()
    {
        using var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(Options) { ["version"] = "0" }, null);
        var listed = await c.ListAsync("*");
        return listed.Files
            .Where(x => !x.Path.EndsWith(Path.DirectorySeparatorChar))
            .ToDictionary(x => Path.GetFileName(x.Path), x => x.Sizes.First());
    }

    private async Task<byte[]> RestoreLatestAsync(string name)
    {
        if (Directory.Exists(RESTOREFOLDER))
            Directory.Delete(RESTOREFOLDER, true);
        Directory.CreateDirectory(RESTOREFOLDER);

        using var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(Options) { ["restore-path"] = RESTOREFOLDER, ["version"] = "0" }, null);
        await c.RestoreAsync(["*"]);
        return File.ReadAllBytes(Directory.GetFiles(RESTOREFOLDER, name, SearchOption.AllDirectories).Single());
    }

    [SetUp]
    public void RegisterProvider()
        => Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new TestProvider());

    [Test]
    [Category("Targeted")]
    public async Task StreamsOfUnknownLengthAreReadOneAtATimeInTheListedOrder()
    {
        var files = Enumerable.Range(0, 8).Select(i => new TestFile("file" + (7 - i), Data(150_000 + i, i), -1)).ToList();
        var result = await BackupAsync(files, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(result.Warnings, Is.Empty, "A stream of unknown length must not give a warning");
        Assert.That(result.Errors, Is.Empty);
        Assert.That(OpenOrder, Is.EqualTo(files.Select(x => x.Name)), "The files must be opened in the order the source lists them");
        Assert.That(MaxOpenStreams, Is.EqualTo(1), "Only one file may be open at a time");

        var listed = await ListLatestAsync();
        foreach (var f in files)
            Assert.That(listed[f.Name], Is.EqualTo(f.Data.Length), "The size is the number of bytes read");

        Assert.That(await RestoreLatestAsync("file3"), Is.EqualTo(files.Single(x => x.Name == "file3").Data));
    }

    [Test]
    [Category("Targeted")]
    public async Task AStreamThatFailsKeepsThePreviousCopyAndStoresWhatWasRead()
    {
        var first = new List<TestFile>
        {
            new("a", Data(50_000, 1), -1),
            new("b", Data(80_000, 2), -1),
            new("c", Data(60_000, 3), -1),
        };
        var result = await BackupAsync(first, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.That(result.Warnings, Is.Empty);

        // In the second run "b" fails after 35,000 bytes, and "c" fails before any data
        var second = new List<TestFile>
        {
            new("a", Data(51_000, 11), -1),
            new("b", Data(90_000, 12), 35_000),
            new("c", Data(70_000, 13), 0),
        };
        result = await BackupAsync(second, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var warnings = result.Warnings.ToList();
        Assert.That(warnings.Count(x => x.Contains("failed after")), Is.EqualTo(2), "Each failed stream gives one warning: " + string.Join("\n", warnings));
        Assert.That(result.Errors, Is.Empty);

        var listed = await ListLatestAsync();
        Assert.That(listed.Keys, Is.EquivalentTo(new[] { "a", "b", "b" + StreamedSource.PartialEntrySuffix, "c" }));
        Assert.That(listed["a"], Is.EqualTo(51_000));
        Assert.That(listed["b"], Is.EqualTo(80_000), "The failed file keeps the copy of the previous backup under its name");
        Assert.That(listed["b" + StreamedSource.PartialEntrySuffix], Is.EqualTo(35_000), "What was read is stored under the partial name");
        Assert.That(listed["c"], Is.EqualTo(60_000));

        Assert.That(await RestoreLatestAsync("b"), Is.EqualTo(first[1].Data));
        Assert.That(await RestoreLatestAsync("c"), Is.EqualTo(first[2].Data));
        Assert.That(await RestoreLatestAsync("b" + StreamedSource.PartialEntrySuffix), Is.EqualTo(second[1].Data.AsSpan(0, 35_000).ToArray()));

        // The next good run replaces the copies and has no partial entries
        var third = new List<TestFile>
        {
            new("a", Data(51_000, 11), -1),
            new("b", Data(90_000, 12), -1),
            new("c", Data(70_000, 13), -1),
        };
        result = await BackupAsync(third, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        Assert.That(result.Warnings, Is.Empty);

        listed = await ListLatestAsync();
        Assert.That(listed.Keys, Is.EquivalentTo(new[] { "a", "b", "c" }));
        Assert.That(listed["b"], Is.EqualTo(90_000));
        Assert.That(await RestoreLatestAsync("b"), Is.EqualTo(third[1].Data));
    }

    [Test]
    [Category("Targeted")]
    public async Task AStreamThatFailsInTheFirstBackupLeavesOnlyThePartialEntry()
    {
        var files = new List<TestFile>
        {
            new("a", Data(50_000, 1), -1),
            new("b", Data(80_000, 2), 25_000),
        };
        var result = await BackupAsync(files, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.That(result.Warnings.Count(x => x.Contains("failed after")), Is.EqualTo(1));

        var listed = await ListLatestAsync();
        Assert.That(listed.Keys, Is.EquivalentTo(new[] { "a", "b" + StreamedSource.PartialEntrySuffix }));
    }
}
