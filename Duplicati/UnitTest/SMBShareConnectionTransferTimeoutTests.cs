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
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.SMB;
using Duplicati.Library.Backend.SMB.Model;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;
using SMBLibrary;
using SMBLibrary.Client;

namespace Duplicati.UnitTest;

/// <summary>
/// The read-write timeout covers each request to the server, and nothing else.
/// A destination or source stream that blocks while the rest of the pipeline catches up
/// must not fail the transfer, while a server that does not answer must.
/// No network: the file store is a proxy.
/// </summary>
public class SMBShareConnectionTransferTimeoutTests : BasicSetupHelper
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private const int ChunkSize = 1024;

    /// <summary>
    /// A file store that holds one file, and answers reads and writes after a configurable delay
    /// </summary>
    public class FileStore : DispatchProxy
    {
        public byte[] Content = Array.Empty<byte>();
        public MemoryStream Written = new MemoryStream();
        public TimeSpan ResponseDelay;
        public int Closed;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(ISMBFileStore.CreateFile):
                    args![0] = new object();
                    args[1] = FileStatus.FILE_OPENED;
                    return NTStatus.STATUS_SUCCESS;

                case nameof(ISMBFileStore.ReadFile):
                    {
                        Thread.Sleep(ResponseDelay);
                        var offset = (long)args![2]!;
                        var maxCount = (int)args[3]!;
                        if (offset >= Content.Length)
                        {
                            args[0] = Array.Empty<byte>();
                            return NTStatus.STATUS_END_OF_FILE;
                        }

                        var count = (int)Math.Min(maxCount, Content.Length - offset);
                        args[0] = Content.AsSpan((int)offset, count).ToArray();
                        return NTStatus.STATUS_SUCCESS;
                    }

                case nameof(ISMBFileStore.WriteFile):
                    {
                        Thread.Sleep(ResponseDelay);
                        var offset = (long)args![2]!;
                        var data = (byte[])args[3]!;
                        lock (Written)
                        {
                            Written.Position = offset;
                            Written.Write(data, 0, data.Length);
                        }
                        args[0] = data.Length;
                        return NTStatus.STATUS_SUCCESS;
                    }

                case nameof(ISMBFileStore.CloseFile):
                    Interlocked.Increment(ref Closed);
                    return NTStatus.STATUS_SUCCESS;

                default:
                    throw new NotImplementedException(targetMethod.Name);
            }
        }
    }

    /// <summary>
    /// A stream that blocks once, on its first read or write, for longer than the timeout
    /// </summary>
    private class BlockingOnceStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly TimeSpan _delay;
        private bool _blocked;

        public BlockingOnceStream(MemoryStream inner, TimeSpan delay)
        {
            _inner = inner;
            _delay = delay;
        }

        private async Task BlockOnceAsync(CancellationToken cancellationToken)
        {
            if (_blocked)
                return;
            _blocked = true;
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await BlockOnceAsync(cancellationToken).ConfigureAwait(false);
            return _inner.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await BlockOnceAsync(cancellationToken).ConfigureAwait(false);
            return _inner.Read(buffer.Span);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await BlockOnceAsync(cancellationToken).ConfigureAwait(false);
            _inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await BlockOnceAsync(cancellationToken).ConfigureAwait(false);
            _inner.Write(buffer.Span);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Only async reads are expected");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Only async writes are expected");
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
    }

    private static (SMBShareConnection Connection, FileStore Store) Create(byte[] content, TimeSpan responseDelay)
    {
        var proxy = DispatchProxy.Create<ISMBFileStore, FileStore>();
        var store = (FileStore)(object)proxy;
        store.Content = content;
        store.ResponseDelay = responseDelay;

        // The client has not negotiated with a server, so set the transfer sizes it would have been given
        var client = new SMB2Client();
        foreach (var field in new[] { "m_maxReadSize", "m_maxWriteSize" })
        {
            var f = typeof(SMB2Client).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Field {field} not found on {nameof(SMB2Client)}");
            f.SetValue(client, Convert.ChangeType(ChunkSize, f.FieldType));
        }

        var connection = new SMBShareConnection(client, proxy,
            new SMBConnectionParameters("server", SMBTransportType.DirectTCPTransport, "share", "folder", null, null, null, null, null, false),
            new TimeoutOptionsHelper.Timeouts(Timeout, Timeout, Timeout));
        return (connection, store);
    }

    private static byte[] MakeContent(int chunks)
    {
        var content = new byte[ChunkSize * chunks];
        new Random(42).NextBytes(content);
        return content;
    }

    [Test]
    [Category("Utility")]
    public async Task GetIsNotFailedByADestinationThatBlocksLongerThanTheTimeout()
    {
        var content = MakeContent(4);
        var (connection, store) = Create(content, TimeSpan.Zero);

        var destination = new MemoryStream();
        await connection.GetAsync("file", new BlockingOnceStream(destination, Timeout * 2), CancellationToken.None);

        Assert.That(destination.ToArray(), Is.EqualTo(content));
        Assert.That(store.Closed, Is.EqualTo(1));
    }

    [Test]
    [Category("Utility")]
    public void GetFailsWhenTheServerDoesNotAnswerWithinTheTimeout()
    {
        var (connection, store) = Create(MakeContent(4), Timeout * 3);

        Assert.ThrowsAsync<TimeoutException>(() => connection.GetAsync("file", new MemoryStream(), CancellationToken.None));
        Assert.That(store.Closed, Is.EqualTo(1), "The handle must be released on failure");
    }

    [Test]
    [Category("Utility")]
    public async Task PutIsNotFailedByASourceThatBlocksLongerThanTheTimeout()
    {
        var content = MakeContent(4);
        var (connection, store) = Create(Array.Empty<byte>(), TimeSpan.Zero);

        await connection.PutAsync("file", new BlockingOnceStream(new MemoryStream(content), Timeout * 2), CancellationToken.None);

        Assert.That(store.Written.ToArray(), Is.EqualTo(content));
        Assert.That(store.Closed, Is.EqualTo(1));
    }

    [Test]
    [Category("Utility")]
    public void PutFailsWhenTheServerDoesNotAnswerWithinTheTimeout()
    {
        var (connection, store) = Create(Array.Empty<byte>(), Timeout * 3);

        Assert.ThrowsAsync<TimeoutException>(() => connection.PutAsync("file", new MemoryStream(MakeContent(4)), CancellationToken.None));
        Assert.That(store.Closed, Is.EqualTo(1), "The handle must be released on failure");
    }
}
