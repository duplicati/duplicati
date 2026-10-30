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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider;
using Duplicati.Library.Utility;
using Duplicati.StreamUtil;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the metadata produced by the <see cref="BackendSourceProvider"/>
/// </summary>
[TestFixture]
public class BackendSourceProviderTests
{
    /// <summary>
    /// The timestamp reported by the stub backend
    /// </summary>
    private static readonly DateTime TIMESTAMP = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    /// <summary>
    /// A backend that lists a single file and reports any folder as existing,
    /// recording the paths it is asked for
    /// </summary>
    private sealed class StubBackend : IFolderEnabledBackend
    {
        public string DisplayName => "Stub Backend";
        public string ProtocolKey => "stub";
        public string Description => "A testing backend";
        public IList<ICommandLineArgument> SupportedCommands => [];

        /// <summary>
        /// The paths passed to <see cref="ListAsync(string, CancellationToken)"/>
        /// </summary>
        public List<string?> ListedPaths { get; } = [];

        /// <summary>
        /// The paths passed to <see cref="GetEntryAsync"/>
        /// </summary>
        public List<string> RequestedEntries { get; } = [];

        public async IAsyncEnumerable<IFileEntry> ListAsync(string? path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ListedPaths.Add(path);
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new FileEntry("file.txt", 42, TIMESTAMP, TIMESTAMP, false, false) { Created = TIMESTAMP };
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
            => ListAsync("", cancellationToken);

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

        public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
        {
            RequestedEntries.Add(path);
            return Task.FromResult<IFileEntry?>(new FileEntry("folder", 0, TIMESTAMP, TIMESTAMP, true, false) { Created = TIMESTAMP });
        }
        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task DeleteAsync(string remotename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => Task.CompletedTask;
        public Task CreateFolderAsync(CancellationToken cancelToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>
    /// A streaming backend that lists a single file of <see cref="ListedSize"/> bytes,
    /// but only delivers <paramref name="deliveredSize"/> bytes without reporting an error,
    /// as happens when an SFTP connection is lost during a download
    /// </summary>
    /// <param name="deliveredSize">The number of bytes written when the file is downloaded</param>
    private sealed class TruncatingBackend(int deliveredSize) : IFolderEnabledBackend, IStreamingBackend
    {
        /// <summary>
        /// The size reported when listing the file
        /// </summary>
        public const int ListedSize = 42;

        public string DisplayName => "Truncating Backend";
        public string ProtocolKey => "truncating";
        public string Description => "A testing backend";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public bool SupportsStreaming => true;

        public async IAsyncEnumerable<IFileEntry> ListAsync(string? path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new FileEntry("file.txt", ListedSize, TIMESTAMP, TIMESTAMP, false, false) { Created = TIMESTAMP };
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
            => ListAsync("", cancellationToken);

        public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
            => stream.WriteAsync(new byte[deliveredSize], 0, deliveredSize, cancelToken);

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());
        public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry?>(new FileEntry("folder", 0, TIMESTAMP, TIMESTAMP, true, false) { Created = TIMESTAMP });
        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task DeleteAsync(string remotename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => Task.CompletedTask;
        public Task CreateFolderAsync(CancellationToken cancelToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>
    /// A streaming backend that downloads through a write timeout without owning the target stream,
    /// as the SSH and WebDAV backends do. Whether a stalled reader makes the download time out depends on the mode.
    /// </summary>
    /// <param name="mode">The time the write timeout counts</param>
    private sealed class StallingBackend(TimeoutMode mode) : IFolderEnabledBackend, IStreamingBackend
    {
        /// <summary>
        /// The size of each write
        /// </summary>
        public const int ChunkSize = 1024 * 1024;

        /// <summary>
        /// The size reported when listing the file
        /// </summary>
        public const int ListedSize = 8 * ChunkSize;

        /// <summary>
        /// The time a single write may take before the download times out
        /// </summary>
        public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(1);

        public string DisplayName => "Stalling Backend";
        public string ProtocolKey => "stalling";
        public string Description => "A testing backend";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public bool SupportsStreaming => true;

        public async IAsyncEnumerable<IFileEntry> ListAsync(string? path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new FileEntry("file.txt", ListedSize, TIMESTAMP, TIMESTAMP, false, false) { Created = TIMESTAMP };
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
            => ListAsync("", cancellationToken);

        /// <summary>
        /// The number of times a download was started
        /// </summary>
        public int Downloads { get; private set; }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            Downloads++;
            using var timeoutStream = stream.ObserveWriteTimeout(WriteTimeout, false, mode);
            var chunk = new byte[ChunkSize];
            for (var written = 0; written < ListedSize; written += ChunkSize)
                await timeoutStream.WriteAsync(chunk, 0, chunk.Length, cancelToken).ConfigureAwait(false);
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());
        public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry?>(new FileEntry("folder", 0, TIMESTAMP, TIMESTAMP, true, false) { Created = TIMESTAMP });
        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task DeleteAsync(string remotename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => Task.CompletedTask;
        public Task CreateFolderAsync(CancellationToken cancelToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>
    /// What a <see cref="ScriptedBackend"/> does on one download
    /// </summary>
    /// <param name="DeliverBytes">The number of bytes delivered before the download ends</param>
    /// <param name="ThenThrow">The exception thrown after the bytes are delivered, or null to end the stream silently</param>
    /// <param name="Content">The content delivered instead of the listed content, or null to deliver the listed content</param>
    private sealed record Attempt(int DeliverBytes, Exception? ThenThrow, byte[]? Content);

    /// <summary>
    /// A streaming backend that lists a single file with the given content and follows a script
    /// for its first downloads. Downloads beyond the script deliver the whole file.
    /// </summary>
    /// <param name="content">The content of the file</param>
    /// <param name="attempts">The script for the first downloads</param>
    private class ScriptedBackend(byte[] content, params Attempt[] attempts) : IFolderEnabledBackend, IStreamingBackend
    {
        /// <summary>
        /// The size of each write
        /// </summary>
        public const int ChunkSize = 4096;

        public string DisplayName => "Scripted Backend";
        public string ProtocolKey => "scripted";
        public string Description => "A testing backend";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public bool SupportsStreaming => true;

        /// <summary>
        /// The number of times a download was started
        /// </summary>
        public int Downloads { get; private set; }

        /// <summary>
        /// The content of the file
        /// </summary>
        protected byte[] Content => content;

        public async IAsyncEnumerable<IFileEntry> ListAsync(string? path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new FileEntry("file.bin", content.Length, TIMESTAMP, TIMESTAMP, false, false) { Created = TIMESTAMP };
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
            => ListAsync("", cancellationToken);

        /// <summary>
        /// Takes the next step of the script, or a complete download once the script is used up
        /// </summary>
        /// <returns>The step to perform</returns>
        protected Attempt NextAttempt()
        {
            var attempt = Downloads < attempts.Length ? attempts[Downloads] : new Attempt(content.Length, null, null);
            Downloads++;
            return attempt;
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var attempt = NextAttempt();
            var data = attempt.Content ?? content;
            for (var written = 0; written < attempt.DeliverBytes; written += ChunkSize)
                await stream.WriteAsync(data, written, Math.Min(ChunkSize, attempt.DeliverBytes - written), cancelToken).ConfigureAwait(false);
            if (attempt.ThenThrow != null)
                throw attempt.ThenThrow;
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());
        public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry?>(new FileEntry("folder", 0, TIMESTAMP, TIMESTAMP, true, false) { Created = TIMESTAMP });
        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task DeleteAsync(string remotename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => Task.CompletedTask;
        public Task CreateFolderAsync(CancellationToken cancelToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>
    /// A stream that delivers the start of a buffer and then throws or ends,
    /// as a response stream does when the connection is lost
    /// </summary>
    /// <param name="data">The buffer to deliver from</param>
    /// <param name="offset">The position in the buffer to start from</param>
    /// <param name="deliverBytes">The number of bytes to deliver before the stream ends</param>
    /// <param name="thenThrow">The exception thrown after the bytes are delivered, or null to end the stream silently</param>
    private sealed class InterruptedStream(byte[] data, int offset, int deliverBytes, Exception? thenThrow) : MemoryStream(data, offset, deliverBytes, false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0 && thenThrow != null)
                throw thenThrow;
            return read;
        }
    }

    /// <summary>
    /// A <see cref="ScriptedBackend"/> that can continue a download from an offset,
    /// recording the offsets and version tokens it is asked for
    /// </summary>
    /// <param name="content">The content of the file</param>
    /// <param name="attempts">The script for the first downloads</param>
    private sealed class ResumableScriptedBackend(byte[] content, params Attempt[] attempts) : ScriptedBackend(content, attempts), IResumeEnabledBackend
    {
        /// <summary>
        /// The version token handed out for the file
        /// </summary>
        public const string ETag = "version-1";

        /// <summary>
        /// The offsets and version tokens passed to <see cref="GetResumableStreamAsync"/>
        /// </summary>
        public List<(long Offset, string? ETag)> Opens { get; } = [];

        public Task<(Stream Stream, string ETag)> GetResumableStreamAsync(string remotename, long offset, string? etag, CancellationToken cancelToken)
        {
            Opens.Add((offset, etag));
            if (etag != null && etag != ETag)
                throw new RemoteFileChangedException("The file has changed");

            var attempt = NextAttempt();
            return Task.FromResult<(Stream, string)>((new InterruptedStream(Content, (int)offset, Math.Min(attempt.DeliverBytes, Content.Length - (int)offset), attempt.ThenThrow), ETag));
        }
    }

    /// <summary>
    /// Creates content that differs from one byte to the next, so a misplaced resume is detected
    /// </summary>
    /// <param name="size">The number of bytes</param>
    /// <returns>The content</returns>
    private static byte[] MakeContent(int size)
    {
        var content = new byte[size];
        new Random(size).NextBytes(content);
        return content;
    }

    /// <summary>
    /// Reads the single file listed by a backend through the source provider, with no delay between resumes
    /// </summary>
    /// <param name="backend">The backend to read from</param>
    /// <returns>The bytes read</returns>
    private static async Task<byte[]> ReadSingleFileAsync(IFolderEnabledBackend backend)
    {
        using var provider = new BackendSourceProvider(backend, "/mnt/stub/", "scripted://host/path") { ResumeDelay = TimeSpan.Zero };
        var root = await provider.EnumerateAsync(CancellationToken.None).SingleAsync();
        var child = await root.Enumerate(CancellationToken.None).SingleAsync();
        using var stream = await child.OpenRead(CancellationToken.None);
        using var target = new MemoryStream();
        await stream.CopyToAsync(target);
        return target.ToArray();
    }

    /// <summary>
    /// Reads the single file listed by a <see cref="TruncatingBackend"/>
    /// </summary>
    /// <param name="deliveredSize">The number of bytes the backend delivers</param>
    /// <returns>The number of bytes read</returns>
    private static async Task<long> ReadTruncatingFileAsync(int deliveredSize)
    {
        using var provider = new BackendSourceProvider(new TruncatingBackend(deliveredSize), "/mnt/stub/", "truncating://host/path") { ResumeDelay = TimeSpan.Zero };
        var root = await provider.EnumerateAsync(CancellationToken.None).SingleAsync();
        var child = await root.Enumerate(CancellationToken.None).SingleAsync();
        using var stream = await child.OpenRead(CancellationToken.None);
        using var target = new MemoryStream();
        await stream.CopyToAsync(target);
        return target.Length;
    }

    /// <summary>
    /// Gets the root entry of a provider wrapping a stub backend
    /// </summary>
    /// <param name="url">The url the backend was created from</param>
    /// <returns>The provider and its root entry</returns>
    private static async Task<(BackendSourceProvider Provider, ISourceProviderEntry Root)> GetRootAsync(string url)
    {
        var provider = new BackendSourceProvider(new StubBackend(), "/mnt/stub/", url);
        var root = await provider.EnumerateAsync(CancellationToken.None).SingleAsync();
        return (provider, root);
    }

    [Test]
    [Category("SourceProvider")]
    public async Task RootMetadataDescribesBackendAsync()
    {
        var (provider, root) = await GetRootAsync("s3://mybucket.s3.amazonaws.com/path?auth-username=user");
        using (provider)
        {
            var metadata = await root.GetMinorMetadata(CancellationToken.None);

            Assert.That(metadata["backend:v"], Is.EqualTo(BackendSourceProvider.METADATA_VERSION));
            Assert.That(metadata["backend:Type"], Is.EqualTo(BackendSourceProvider.METADATA_ROOT_TYPE));
            Assert.That(metadata["backend:Name"], Is.EqualTo("Stub Backend"));
            Assert.That(metadata["backend:Protocol"], Is.EqualTo("stub"));
            Assert.That(metadata["backend:Host"], Is.EqualTo(".amazonaws.com"));

            // The backup process adds to the result, so each call must return its own instance
            Assert.That(await root.GetMinorMetadata(CancellationToken.None), Is.Not.SameAs(metadata));
        }
    }

    [Test]
    [Category("SourceProvider")]
    public async Task RootMetadataOmitsUnknownS3HostAsync()
    {
        var (provider, root) = await GetRootAsync("s3://nas.internal.example.com/path");
        using (provider)
        {
            var metadata = await root.GetMinorMetadata(CancellationToken.None);

            Assert.That(metadata["backend:Protocol"], Is.EqualTo("stub"));
            Assert.That(metadata.ContainsKey("backend:Host"), Is.False);
        }
    }

    [Test]
    [Category("SourceProvider")]
    public async Task RootMetadataOmitsHostForNonS3Async()
    {
        // Only S3 urls have the host reported, even if the host is a known public server
        var (provider, root) = await GetRootAsync("ssh://files.s3.amazonaws.com/path");
        using (provider)
        {
            var metadata = await root.GetMinorMetadata(CancellationToken.None);

            Assert.That(metadata["backend:Protocol"], Is.EqualTo("stub"));
            Assert.That(metadata.ContainsKey("backend:Host"), Is.False);
        }
    }

    /// <summary>
    /// The root mount point used when listing for the user interface
    /// </summary>
    private static readonly string SEP = Util.DirectorySeparatorString;

    [Test]
    [Category("SourceProvider")]
    public async Task ChildPathsAreRootedWhenMountedAtRootAsync()
    {
        // The user interface lists sources mounted at the root,
        // and expects rooted paths like the other source providers return
        var backend = new StubBackend();
        using var provider = new BackendSourceProvider(backend, SEP, "stub://host/path");
        var root = await provider.EnumerateAsync(CancellationToken.None).SingleAsync();
        var child = await root.Enumerate(CancellationToken.None).SingleAsync();

        Assert.That(child.Path, Is.EqualTo(SEP + "file.txt"));
        Assert.That(backend.ListedPaths, Is.EqualTo(new[] { "" }));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task ChildPathsIncludeMountPointWhenMountedAsync()
    {
        var backend = new StubBackend();
        using var provider = new BackendSourceProvider(backend, "/mnt/stub/", "stub://host/path");
        var root = await provider.EnumerateAsync(CancellationToken.None).SingleAsync();
        var child = await root.Enumerate(CancellationToken.None).SingleAsync();

        Assert.That(child.Path, Is.EqualTo(SystemIO.IO_OS.PathCombine("/mnt/stub/", "file.txt")));
        Assert.That(backend.ListedPaths, Is.EqualTo(new[] { "" }));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task GetEntryAcceptsRootedPathWhenMountedAtRootAsync()
    {
        // The user interface sends back the rooted path it was given; the backend must see a relative path
        var backend = new StubBackend();
        using var provider = new BackendSourceProvider(backend, SEP, "stub://host/path");
        var entry = await provider.GetEntryAsync(SEP + "folder" + SEP, true, CancellationToken.None);

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.IsFolder, Is.True);
        Assert.That(entry.Path, Is.EqualTo(SEP + "folder" + SEP));
        Assert.That(backend.RequestedEntries, Is.EqualTo(new[] { "folder/" }));

        // Listing the entry must also use the relative path, and produce rooted children
        var child = await entry.Enumerate(CancellationToken.None).SingleAsync();
        Assert.That(backend.ListedPaths, Is.EqualTo(new[] { "folder/" }));
        Assert.That(child.Path, Is.EqualTo(SEP + "folder" + SEP + "file.txt"));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task GetEntryAcceptsRelativePathWhenMountedAtRootAsync()
    {
        // Older clients send the path as the backend reported it
        var backend = new StubBackend();
        using var provider = new BackendSourceProvider(backend, SEP, "stub://host/path");
        var entry = await provider.GetEntryAsync("folder/", true, CancellationToken.None);

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Path, Is.EqualTo(SEP + "folder" + SEP));
        Assert.That(backend.RequestedEntries, Is.EqualTo(new[] { "folder/" }));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task GetEntryStripsMountPointWhenMountedAsync()
    {
        var backend = new StubBackend();
        using var provider = new BackendSourceProvider(backend, "/mnt/stub/", "stub://host/path");
        var entry = await provider.GetEntryAsync("/mnt/stub/folder/", true, CancellationToken.None);

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Path, Is.EqualTo(SystemIO.IO_OS.PathCombine("/mnt/stub/", "folder" + SEP)));
        Assert.That(backend.RequestedEntries, Is.EqualTo(new[] { "folder/" }));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task ChildEntriesCarryBackendTimestampsAsync()
    {
        var (provider, root) = await GetRootAsync("stub://host/path");
        using (provider)
        {
            var child = await root.Enumerate(CancellationToken.None).SingleAsync();

            Assert.That(child.LastModificationUtc, Is.EqualTo(TIMESTAMP));
            Assert.That(child.CreatedUtc, Is.EqualTo(TIMESTAMP));
            Assert.That(child.Size, Is.EqualTo(42));
            Assert.That(await child.GetMinorMetadata(CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    [Category("SourceProvider")]
    public async Task CompleteStreamedReadSucceedsAsync()
    {
        Assert.That(await ReadTruncatingFileAsync(TruncatingBackend.ListedSize), Is.EqualTo(TruncatingBackend.ListedSize));
    }

    [Test]
    [Category("SourceProvider")]
    public void TruncatedStreamedReadThrows()
    {
        Assert.ThrowsAsync<IOException>(() => ReadTruncatingFileAsync(10));
    }

    [Test]
    [Category("SourceProvider")]
    public void EmptyStreamedReadOfNonEmptyFileThrows()
    {
        Assert.ThrowsAsync<IOException>(() => ReadTruncatingFileAsync(0));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task TimedOutStreamedReadIsResumedAsync()
    {
        var backend = new StallingBackend(TimeoutMode.CallAndGap);
        using var provider = new BackendSourceProvider(backend, "/mnt/stub/", "stalling://host/path") { ResumeDelay = TimeSpan.Zero };
        var root = await provider.EnumerateAsync(CancellationToken.None).SingleAsync();
        var child = await root.Enumerate(CancellationToken.None).SingleAsync();
        using var stream = await child.OpenRead(CancellationToken.None);

        // Stall the reader past the write timeout while the download is still in progress.
        // The download fails with a timeout, and the stream must start it again and
        // continue from the bytes already read, so the whole file still arrives.
        var buffer = new byte[StallingBackend.ChunkSize];
        await stream.ReadAtLeastAsync(buffer, buffer.Length);
        await Task.Delay(StallingBackend.WriteTimeout * 2);

        var remaining = new MemoryStream();
        await stream.CopyToAsync(remaining);
        Assert.That(buffer.Length + remaining.Length, Is.EqualTo(StallingBackend.ListedSize));
        Assert.That(backend.Downloads, Is.EqualTo(2));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task StalledReaderDoesNotTimeOutAGapModeDownloadAsync()
    {
        using var provider = new BackendSourceProvider(new StallingBackend(TimeoutMode.Gap), "/mnt/stub/", "stalling://host/path");
        var root = await provider.EnumerateAsync(CancellationToken.None).SingleAsync();
        var child = await root.Enumerate(CancellationToken.None).SingleAsync();
        using var stream = await child.OpenRead(CancellationToken.None);

        // The reader is held back by the rest of the pipeline for longer than the write timeout.
        // The backend is blocked inside its write to the pipe, which is this side of the transfer,
        // so the wait must not count against the timeout and the whole file must arrive.
        var buffer = new byte[StallingBackend.ChunkSize];
        await stream.ReadAtLeastAsync(buffer, buffer.Length);
        await Task.Delay(StallingBackend.WriteTimeout * 2);

        var remaining = new MemoryStream();
        await stream.CopyToAsync(remaining);
        Assert.That(buffer.Length + remaining.Length, Is.EqualTo(StallingBackend.ListedSize));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task FailedStreamedReadResumesFromThePositionReachedAsync()
    {
        var content = MakeContent(50 * ScriptedBackend.ChunkSize);
        var backend = new ScriptedBackend(content, new Attempt(20 * ScriptedBackend.ChunkSize, new IOException("Connection closed"), null));

        Assert.That(await ReadSingleFileAsync(backend), Is.EqualTo(content));
        Assert.That(backend.Downloads, Is.EqualTo(2));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task SilentlyTruncatedStreamedReadIsResumedAsync()
    {
        var content = MakeContent(50 * ScriptedBackend.ChunkSize);
        var backend = new ScriptedBackend(content,
            new Attempt(20 * ScriptedBackend.ChunkSize, null, null),
            new Attempt(35 * ScriptedBackend.ChunkSize, null, null));

        Assert.That(await ReadSingleFileAsync(backend), Is.EqualTo(content));
        Assert.That(backend.Downloads, Is.EqualTo(3));
    }

    [Test]
    [Category("SourceProvider")]
    public void ChangedContentFailsTheResumeWithoutAnotherAttempt()
    {
        var content = MakeContent(50 * ScriptedBackend.ChunkSize);
        var changed = MakeContent(50 * ScriptedBackend.ChunkSize + 1);
        var backend = new ScriptedBackend(content,
            new Attempt(20 * ScriptedBackend.ChunkSize, new IOException("Connection closed"), null),
            new Attempt(50 * ScriptedBackend.ChunkSize, null, changed));

        Assert.ThrowsAsync<RemoteFileChangedException>(() => ReadSingleFileAsync(backend));
        Assert.That(backend.Downloads, Is.EqualTo(2));
    }

    [Test]
    [Category("SourceProvider")]
    public void ExhaustedResumesReportTheLastFailure()
    {
        var content = MakeContent(10 * ScriptedBackend.ChunkSize);
        var attempts = Enumerable.Range(0, 10).Select(_ => new Attempt(ScriptedBackend.ChunkSize, new IOException("Connection closed"), null)).ToArray();
        var backend = new ScriptedBackend(content, attempts);
        using var provider = new BackendSourceProvider(backend, "/mnt/stub/", "scripted://host/path") { ResumeDelay = TimeSpan.Zero, ResumeAttempts = 3 };

        var ex = Assert.ThrowsAsync<IOException>(async () =>
        {
            var root = await provider.EnumerateAsync(CancellationToken.None).SingleAsync();
            var child = await root.Enumerate(CancellationToken.None).SingleAsync();
            using var stream = await child.OpenRead(CancellationToken.None);
            await stream.CopyToAsync(Stream.Null);
        });

        Assert.That(ex!.Message, Is.EqualTo("Connection closed"));
        Assert.That(backend.Downloads, Is.EqualTo(4));
    }

    [Test]
    [Category("SourceProvider")]
    public async Task ResumeEnabledBackendIsAskedForTheRemainderUnderItsETagAsync()
    {
        var content = MakeContent(50 * ScriptedBackend.ChunkSize);
        var failAt = 20 * ScriptedBackend.ChunkSize;
        var backend = new ResumableScriptedBackend(content, new Attempt(failAt, new IOException("Connection closed"), null));

        Assert.That(await ReadSingleFileAsync(backend), Is.EqualTo(content));
        Assert.That(backend.Downloads, Is.EqualTo(2));
        Assert.That(backend.Opens, Is.EqualTo(new[] { (0L, (string?)null), ((long)failAt, ResumableScriptedBackend.ETag) }));
    }

    [Test]
    [Category("SourceProvider")]
    public void ResumeEnabledBackendRejectingTheETagFailsTheRead()
    {
        var content = MakeContent(50 * ScriptedBackend.ChunkSize);
        var backend = new ChangingResumableBackend(content, new Attempt(20 * ScriptedBackend.ChunkSize, new IOException("Connection closed"), null));

        Assert.ThrowsAsync<RemoteFileChangedException>(() => ReadSingleFileAsync(backend));
        Assert.That(backend.Opens, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// A resume-enabled backend whose file changes version after the first download
    /// </summary>
    /// <param name="content">The content of the file</param>
    /// <param name="attempts">The script for the first downloads</param>
    private sealed class ChangingResumableBackend(byte[] content, params Attempt[] attempts) : ScriptedBackend(content, attempts), IResumeEnabledBackend
    {
        /// <summary>
        /// The offsets and version tokens passed to <see cref="GetResumableStreamAsync"/>
        /// </summary>
        public List<(long Offset, string? ETag)> Opens { get; } = [];

        public Task<(Stream Stream, string ETag)> GetResumableStreamAsync(string remotename, long offset, string? etag, CancellationToken cancelToken)
        {
            Opens.Add((offset, etag));
            if (etag != null)
                throw new RemoteFileChangedException("The file is now version-2");

            var attempt = NextAttempt();
            return Task.FromResult<(Stream, string)>((new InterruptedStream(Content, 0, attempt.DeliverBytes, attempt.ThenThrow), "version-1"));
        }
    }
}
