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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// "Stop now" while a source file read is stuck, as a read from a network share that stopped
/// answering can be. Not every stream ends a stuck read when the token is cancelled; on Linux a
/// file opened for synchronous reads does not. The backup then waited for the read for ever.
/// The stream here ignores the token, as those do.
/// </summary>
[NonParallelizable]
public class AbortedSourceReadTests : BasicSetupHelper
{
    /// <summary>
    /// A file whose reads stop answering after the first blocks, and ignore cancellation,
    /// until released
    /// </summary>
    private sealed class StallingStream : Stream
    {
        private const long FileLength = 10 * 1024 * 1024;
        private long m_position;

        public static readonly ManualResetEventSlim Release = new(false);
        public static int StalledReads;
        public static int ReadsAfterRelease;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_position >= 64 * 1024)
            {
                Interlocked.Increment(ref StalledReads);
                Release.Wait();
                Interlocked.Increment(ref ReadsAfterRelease);
            }

            var n = (int)Math.Min(count, FileLength - m_position);
            Array.Fill(buffer, (byte)(m_position & 0xff), offset, n);
            m_position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => FileLength;
        public override long Position { get => m_position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StallingEntry : ISourceProviderEntry
    {
        public bool IsFolder { get; init; }
        public bool IsMetaEntry => false;
        public bool IsRootEntry { get; init; }
        public DateTime CreatedUtc => new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateTime LastModificationUtc => new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string Path { get; init; } = string.Empty;
        public long Size => IsFolder ? 0 : 10 * 1024 * 1024;
        public bool IsSymlink => false;
        public string? SymlinkTarget => null;
        public FileAttributes Attributes => IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
        public bool IsBlockDevice => false;
        public bool IsCharacterDevice => false;
        public bool IsAlternateStream => false;
        public string? HardlinkTargetId => null;
        public ISourceProviderEntry? Child { get; init; }

        public Task<Stream> OpenRead(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new StallingStream());

        public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>());

        public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            if (Child != null)
                yield return Child;
        }
    }

    private sealed class StallingSourceProvider : ISourceProviderModule
    {
        private readonly string _mountPoint;

        public StallingSourceProvider()
        {
            _mountPoint = string.Empty;
        }

        public StallingSourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
        {
            _mountPoint = mountPoint;
        }

        public string Key => "test-stalling-read";
        public string DisplayName => "Test stalling read provider";
        public string Description => "Test provider whose file reads stop answering";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public string MountedPath => _mountPoint;
        public bool NeedsStoredMetadata => false;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new StallingEntry
            {
                Path = _mountPoint,
                IsFolder = true,
                IsRootEntry = true,
                Child = new StallingEntry { Path = _mountPoint + "stuck.bin" }
            };
        }

        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
            => Task.FromResult<ISourceProviderEntry?>(null);

        public void Dispose() { }
    }

    [Test]
    [Category("Targeted")]
    public async Task AbortedBackupWithAStuckSourceReadReturnsAsync()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new StallingSourceProvider());
        StallingStream.Release.Reset();
        StallingStream.StalledReads = 0;
        StallingStream.ReadsAfterRelease = 0;

        var options = new Dictionary<string, string>(TestOptions)
        {
            ["no-encryption"] = "true",
        };

        try
        {
            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            var backupTask = Task.Run(async () => await c.BackupAsync(["@/stalling|test-stalling-read://source"]));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (StallingStream.StalledReads == 0 && sw.Elapsed < TimeSpan.FromMinutes(1) && !backupTask.IsCompleted)
                await Task.Delay(50);
            Assert.That(StallingStream.StalledReads, Is.EqualTo(1), "The source read did not get stuck");

            await c.AbortAsync();

            var stopped = await Task.WhenAny(backupTask, Task.Delay(TimeSpan.FromSeconds(30))) == backupTask;
            Assert.That(stopped, Is.True, "The abort did not make the backup return within 30 seconds");
            Assert.That(async () => await backupTask, Throws.InstanceOf<OperationCanceledException>(),
                "An aborted backup should end with the cancellation");

            // The read that was left behind ends later; that must not disturb anything
            StallingStream.Release.Set();
            sw.Restart();
            while (StallingStream.ReadsAfterRelease == 0 && sw.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(50);
            Assert.That(StallingStream.ReadsAfterRelease, Is.EqualTo(1), "The read left behind should end once released");

            // The local database is closed once the backup has returned
            using var fs = new FileStream(options["dbpath"], FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            StallingStream.Release.Set();
        }
    }
}
