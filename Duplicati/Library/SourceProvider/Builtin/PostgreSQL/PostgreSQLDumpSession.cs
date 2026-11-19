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

using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// One run of pg_dump, read forward as a stream. The data blocks can only be handed out in
/// the order they appear, each once. A block that is asked for after blocks that nobody read
/// makes those blocks be read and discarded.
/// </summary>
public sealed class PostgreSQLDumpSession : IDisposable
{
    private readonly PostgreSQLClientTools.IDumpOutput m_tool;
    private readonly PostgreSQLArchiveReader m_reader;
    private readonly HashSet<int> m_passed = [];
    private readonly HashSet<int> m_opened = [];
    private readonly int m_dataBlocks;
    private int m_blocksSeen;
    private int? m_current;
    private bool m_inLargeObjects;
    private int m_largeObjectHeaderOffset;
    private byte[]? m_largeObjectHeader;
    private bool m_ended;
    private Exception? m_failure;

    /// <summary>The archive reader, with the table of contents</summary>
    public PostgreSQLArchiveReader Reader => m_reader;

    private PostgreSQLDumpSession(PostgreSQLClientTools.IDumpOutput tool, PostgreSQLArchiveReader reader)
    {
        m_tool = tool;
        m_reader = reader;
        m_dataBlocks = reader.Entries.Count(x => x.HasData);
    }

    /// <summary>
    /// Starts pg_dump and reads the header and the table of contents
    /// </summary>
    /// <param name="tool">The started pg_dump</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The session</returns>
    public static async Task<PostgreSQLDumpSession> StartAsync(PostgreSQLClientTools.IDumpOutput tool, CancellationToken cancellationToken)
    {
        try
        {
            var reader = await PostgreSQLArchiveReader.OpenAsync(tool.Output, cancellationToken).ConfigureAwait(false);
            return new PostgreSQLDumpSession(tool, reader);
        }
        catch (EndOfStreamException)
        {
            // The program ended before it wrote a table of contents; what it wrote as errors says why
            var (exitCode, errorOutput) = await tool.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            tool.Dispose();
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(tool.Tool, exitCode, errorOutput), "PostgreSQLToolFailed");
        }
        catch
        {
            tool.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Marks an entry as opened; each can be opened once
    /// </summary>
    /// <param name="dumpId">The dump id, or the id used for the large objects</param>
    /// <returns>False if the entry was opened before</returns>
    public bool TryOpen(int dumpId)
        => m_opened.Add(dumpId);

    /// <summary>
    /// Reads rows of a table
    /// </summary>
    /// <param name="dumpId">The dump id of the table data</param>
    /// <param name="path">The entry path, for messages</param>
    /// <param name="buffer">The buffer to read into</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The number of bytes read, or zero at the end of the table</returns>
    public async ValueTask<int> ReadTableAsync(int dumpId, string path, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ThrowIfFailed();

        try
        {
            if (m_current != dumpId)
            {
                if (m_passed.Contains(dumpId))
                    throw new InvalidOperationException(Strings.PostgreSQLSourceProvider.TablePassed(path));
                if (m_inLargeObjects)
                    throw new InvalidOperationException(Strings.PostgreSQLSourceProvider.TableNotInDump(path));

                await LeaveCurrentBlockAsync(cancellationToken).ConfigureAwait(false);

                while (true)
                {
                    var header = await m_reader.ReadBlockHeaderAsync(cancellationToken).ConfigureAwait(false);
                    if (header == null)
                    {
                        await VerifyEndAsync(cancellationToken).ConfigureAwait(false);
                        throw new InvalidOperationException(Strings.PostgreSQLSourceProvider.TableNotInDump(path));
                    }

                    if (header.Value.Type == PostgreSQLArchiveReader.BlockTypeLargeObjects)
                    {
                        // The large objects are the last part of the archive, so the table is not in it
                        EnterLargeObjects();
                        throw new InvalidOperationException(Strings.PostgreSQLSourceProvider.TableNotInDump(path));
                    }

                    if (header.Value.DumpId == dumpId)
                    {
                        m_current = dumpId;
                        break;
                    }

                    // A block nobody asked for before this one was skipped by the backup
                    await m_reader.DiscardBlockAsync(cancellationToken).ConfigureAwait(false);
                    BlockDone(header.Value.DumpId);
                }
            }

            var read = await m_reader.ReadBlockDataAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                BlockDone(dumpId);
                m_current = null;

                // After the last block the program must have ended well, or the dump cannot be trusted
                if (m_blocksSeen == m_dataBlocks)
                    await ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            return read;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            throw await FailAsync(ex, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the large objects, as the rest of the archive from the first block of them
    /// </summary>
    /// <param name="buffer">The buffer to read into</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The number of bytes read, or zero at the end</returns>
    public async ValueTask<int> ReadLargeObjectsAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ThrowIfFailed();
        if (m_ended || buffer.Length == 0)
            return 0;

        try
        {
            if (!m_inLargeObjects)
            {
                await LeaveCurrentBlockAsync(cancellationToken).ConfigureAwait(false);

                while (!m_inLargeObjects)
                {
                    var header = await m_reader.ReadBlockHeaderAsync(cancellationToken).ConfigureAwait(false);
                    if (header == null)
                    {
                        await VerifyEndAsync(cancellationToken).ConfigureAwait(false);
                        return 0;
                    }

                    if (header.Value.Type == PostgreSQLArchiveReader.BlockTypeLargeObjects)
                        EnterLargeObjects();
                    else
                    {
                        await m_reader.DiscardBlockAsync(cancellationToken).ConfigureAwait(false);
                        BlockDone(header.Value.DumpId);
                    }
                }
            }

            // The header of the first block was read to find it, so it is handed out first
            if (m_largeObjectHeader != null && m_largeObjectHeaderOffset < m_largeObjectHeader.Length)
            {
                var count = Math.Min(m_largeObjectHeader.Length - m_largeObjectHeaderOffset, buffer.Length);
                m_largeObjectHeader.AsSpan(m_largeObjectHeaderOffset, count).CopyTo(buffer.Span);
                m_largeObjectHeaderOffset += count;
                return count;
            }

            var read = await m_reader.ReadRawAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                await VerifyEndAsync(cancellationToken).ConfigureAwait(false);
            return read;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            throw await FailAsync(ex, cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnterLargeObjects()
    {
        m_inLargeObjects = true;
        m_largeObjectHeader = m_reader.LastBlockHeader;
        m_largeObjectHeaderOffset = 0;
    }

    private void BlockDone(int dumpId)
    {
        m_passed.Add(dumpId);
        m_blocksSeen++;
    }

    /// <summary>
    /// Discards what is left of a block whose reader stopped before the end
    /// </summary>
    private async Task LeaveCurrentBlockAsync(CancellationToken cancellationToken)
    {
        if (m_current == null)
            return;

        await m_reader.DiscardBlockAsync(cancellationToken).ConfigureAwait(false);
        BlockDone(m_current.Value);
        m_current = null;
    }

    /// <summary>
    /// Reads past the last block, where the archive must end
    /// </summary>
    private async Task ReadToEndAsync(CancellationToken cancellationToken)
    {
        if (await m_reader.ReadBlockHeaderAsync(cancellationToken).ConfigureAwait(false) != null)
            throw new IOException(Strings.PostgreSQLSourceProvider.UnexpectedArchive("data follows the last block"));

        await VerifyEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks at the end of the archive that pg_dump ended without an error
    /// </summary>
    private async Task VerifyEndAsync(CancellationToken cancellationToken)
    {
        if (m_ended)
            return;

        m_ended = true;
        var (exitCode, errorOutput) = await m_tool.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
            throw new IOException(Strings.PostgreSQLSourceProvider.ToolFailed(m_tool.Tool, exitCode, errorOutput));
    }

    /// <summary>
    /// Records that the dump failed. Every later read fails with the same error, so each
    /// entry after the failure is reported, and none is read from a broken stream.
    /// </summary>
    private async Task<Exception> FailAsync(Exception ex, CancellationToken cancellationToken)
    {
        if (m_failure != null)
            return m_failure;

        var message = ex.Message;
        if (ex is EndOfStreamException)
        {
            // The stream ended inside the archive; the reason is in what the program wrote as errors
            try
            {
                var (exitCode, errorOutput) = await m_tool.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                message = Strings.PostgreSQLSourceProvider.ToolFailed(m_tool.Tool, exitCode, errorOutput);
            }
            catch (Exception waitException) when (waitException is not OperationCanceledException)
            {
            }
        }

        m_ended = true;
        m_failure = new IOException(Strings.PostgreSQLSourceProvider.DumpEndedEarly(message), ex);
        return m_failure;
    }

    private void ThrowIfFailed()
    {
        if (m_failure != null)
            throw new IOException(m_failure.Message, m_failure);
    }

    /// <inheritdoc />
    public void Dispose()
        => m_tool.Dispose();
}

/// <summary>
/// The stream of a table or of the large objects. Its length is not known before it is read.
/// </summary>
public sealed class PostgreSQLDumpStream : Stream, IStreamedSourceStream
{
    private readonly Func<Memory<byte>, CancellationToken, ValueTask<int>> m_source;
    private readonly PostgreSQLSegmenter? m_segmenter;
    private readonly Exception? m_openFailure;
    private readonly byte[] m_input = new byte[128 * 1024];
    private int m_inputPosition;
    private int m_inputLength;
    private bool m_sourceEnded;
    private long m_position;

    /// <summary>
    /// Creates the stream
    /// </summary>
    /// <param name="source">Reads the data</param>
    /// <param name="segmenter">The segmenter that pads rows to block boundaries, or null to pass the data on unchanged</param>
    /// <param name="openFailure">An error to fail the first read with, or null</param>
    public PostgreSQLDumpStream(Func<Memory<byte>, CancellationToken, ValueTask<int>> source, PostgreSQLSegmenter? segmenter, Exception? openFailure)
    {
        m_source = source;
        m_segmenter = segmenter;
        m_openFailure = openFailure;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        // The error is raised by the read, and not when the entry is opened, so the backup
        // treats it as a stream that failed and keeps the copy from the previous backup
        if (m_openFailure != null)
            throw new IOException(m_openFailure.Message, m_openFailure);

        if (m_segmenter == null)
        {
            var read = await m_source(buffer, cancellationToken).ConfigureAwait(false);
            m_position += read;
            return read;
        }

        var written = 0;
        while (written < buffer.Length)
        {
            if (m_segmenter.PendingPadding > 0)
            {
                written += m_segmenter.WritePadding(buffer.Span[written..]);
                continue;
            }

            if (m_inputPosition == m_inputLength)
            {
                // Hand back what there is before waiting for more
                if (m_sourceEnded || written > 0)
                    break;

                m_inputPosition = 0;
                m_inputLength = await m_source(m_input, cancellationToken).ConfigureAwait(false);
                if (m_inputLength == 0)
                {
                    m_sourceEnded = true;
                    break;
                }
            }

            var copied = m_segmenter.CopyRows(m_input.AsSpan(m_inputPosition, m_inputLength - m_inputPosition), buffer.Span[written..]);
            m_inputPosition += copied;
            written += copied;
        }

        m_position += written;
        return written;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override bool CanRead => true;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Position { get => m_position; set => throw new NotSupportedException(); }
    /// <inheritdoc />
    public override void Flush() { }
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
