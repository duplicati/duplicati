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


using System.Buffers;
using System.Text;

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// The base of the streams a restore writes to. The restore engine writes a file from its
/// start to its end when the file does not exist, which is what the provider reports, so
/// the stream only moves forward.
/// </summary>
internal abstract class PostgreSQLRestoreStream : Stream
{
    /// <summary>The number of bytes written to the stream</summary>
    protected long m_written;

    /// <inheritdoc />
    public override bool CanRead => false;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => true;
    /// <summary>The bytes written, which is the length the restore engine compares with the length of the file</summary>
    public override long Length => m_written;

    /// <inheritdoc />
    public override long Position
    {
        get => m_written;
        set
        {
            if (value != m_written)
                throw new NotSupportedException("The entry can only be written from its start to its end");
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    /// <summary>
    /// The length of the data is whatever is written, so setting it does nothing
    /// </summary>
    /// <param name="value">The length</param>
    public override void SetLength(long value)
    {
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
}

/// <summary>
/// Collects the table of contents entry and hands it over when it is closed
/// </summary>
internal sealed class PostgreSQLTableOfContentsStream(Action<byte[]> completed) : PostgreSQLRestoreStream
{
    private readonly MemoryStream m_data = new();
    private bool m_disposed;

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        m_data.Write(buffer.Span);
        m_written += buffer.Length;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !m_disposed)
        {
            m_disposed = true;
            completed(m_data.ToArray());
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Accepts and drops what is written, for an entry that is not restored
/// </summary>
internal sealed class PostgreSQLDiscardStream : PostgreSQLRestoreStream
{
    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        m_written += buffer.Length;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Takes the padding out of the rows of a table as they were stored, and finds their end.
/// The rows are COPY text, where a line with only a backslash and a dot ends the data. A row
/// cannot be such a line, as a backslash in a value is written as two.
/// </summary>
public sealed class PostgreSQLRowFilter
{
    private const byte Padding = PostgreSQLSegmenter.Padding;
    private const byte NewLine = (byte)'\n';
    private const byte Backslash = (byte)'\\';
    private const byte Dot = (byte)'.';

    private readonly string m_table;
    private bool m_atLineStart = true;
    /// <summary>How much of the end marker the current line has matched</summary>
    private int m_markerMatched;

    /// <summary>True when the end marker has passed</summary>
    public bool EndSeen { get; private set; }

    /// <summary>
    /// Creates the filter
    /// </summary>
    /// <param name="table">The table, for messages</param>
    public PostgreSQLRowFilter(string table)
    {
        m_table = table;
    }

    /// <summary>
    /// Copies the rows without the padding, up to and including the end marker. What reads the
    /// rows takes statements again after the marker, so nothing that follows it is passed on.
    /// </summary>
    /// <param name="input">The stored data</param>
    /// <param name="output">Where the rows go; at least as long as the input</param>
    /// <returns>The number of bytes to pass on</returns>
    public int Filter(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var length = 0;
        foreach (var b in input)
        {
            if (b == Padding)
                continue;

            if (EndSeen)
            {
                // pg_dump ends the rows of a table with the marker and empty lines
                if (b != NewLine)
                    throw new IOException(Strings.PostgreSQLRestoreProvider.DataAfterEnd(m_table));
                continue;
            }

            output[length++] = b;

            if (m_markerMatched == 2)
            {
                if (b == NewLine)
                    EndSeen = true;
                m_markerMatched = 0;
            }
            else if (m_markerMatched == 1)
                m_markerMatched = b == Dot ? 2 : 0;
            else if (m_atLineStart && b == Backslash)
                m_markerMatched = 1;

            m_atLineStart = b == NewLine;
        }

        return length;
    }
}

/// <summary>
/// Loads the rows of one table: what is written goes to a psql that runs COPY inside a
/// transaction, without the padding between the row segments. The transaction is committed
/// when the end marker of the rows has arrived and the load succeeded. If the stream is
/// closed before that, psql is stopped and the server rolls the transaction back.
/// </summary>
internal sealed class PostgreSQLTableStream : PostgreSQLRestoreStream
{
    /// <summary>The statement that ends the transaction of a table that loaded</summary>
    private static readonly byte[] Commit = "COMMIT;\n"u8.ToArray();

    private readonly PostgreSQLClientTools.RunningTool m_psql;
    private readonly PostgreSQLRowFilter m_filter;
    private readonly Action m_loaded;
    private readonly Action<string> m_incomplete;
    private readonly string m_table;
    private bool m_committed;
    /// <summary>True when the failure of the load was already reported to the restore engine</summary>
    private bool m_failed;

    /// <summary>
    /// Creates the stream
    /// </summary>
    /// <param name="psql">A psql that has read the statements up to and including the COPY statement</param>
    /// <param name="table">The table, for messages</param>
    /// <param name="loaded">Called when the rows are committed</param>
    /// <param name="incomplete">Called when the stream is closed before the rows were committed, and no write reported why</param>
    public PostgreSQLTableStream(PostgreSQLClientTools.RunningTool psql, string table, Action loaded, Action<string> incomplete)
    {
        m_psql = psql;
        m_table = table;
        m_filter = new PostgreSQLRowFilter(table);
        m_loaded = loaded;
        m_incomplete = incomplete;
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        m_written += buffer.Length;

        var output = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            int length;
        try
        {
            length = m_filter.Filter(buffer.Span, output);
        }
        catch (IOException)
        {
            m_failed = true;
            throw;
        }
            if (length > 0)
                await SendAsync(output.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(output);
        }

        if (m_filter.EndSeen && !m_committed)
            await CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        try
        {
            await m_psql.Input.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // psql ended, which it does on the first error; what it wrote says why
            throw await FailureAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        await SendAsync(Commit, cancellationToken).ConfigureAwait(false);
        try
        {
            await m_psql.Input.FlushAsync(cancellationToken).ConfigureAwait(false);
            m_psql.Input.Close();
        }
        catch (IOException)
        {
            throw await FailureAsync(cancellationToken).ConfigureAwait(false);
        }

        var (exitCode, errorOutput) = await m_psql.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            m_failed = true;
            throw new IOException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.Psql, exitCode, errorOutput));
        }

        m_committed = true;
        m_loaded();
    }

    private async Task<IOException> FailureAsync(CancellationToken cancellationToken)
    {
        m_failed = true;
        var (exitCode, errorOutput) = await m_psql.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new IOException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.Psql, exitCode, errorOutput));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // Stopping a psql that has not committed makes the server roll the transaction back
        if (disposing)
        {
            m_psql.Dispose();

            // Rows that ended before their end marker were not loaded, which nothing else reports
            if (!m_committed && !m_failed)
            {
                m_failed = true;
                m_incomplete(Strings.PostgreSQLRestoreProvider.TableIncomplete(m_table));
            }
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Restores the large objects: what is written is the part of the archive that holds them,
/// and it goes to a pg_restore that has already read the table of contents. The outcome is
/// only known when the stream is closed and pg_restore ends, so it is reported to the provider.
/// </summary>
internal sealed class PostgreSQLLargeObjectStream : PostgreSQLRestoreStream
{
    private readonly PostgreSQLClientTools.RunningTool m_restore;
    private readonly Action<string?> m_completed;
    private bool m_disposed;
    private bool m_failed;

    /// <summary>
    /// Creates the stream
    /// </summary>
    /// <param name="restore">A pg_restore that reads the archive from its standard input, and was given the table of contents</param>
    /// <param name="completed">Called when pg_restore has ended, with what went wrong or null</param>
    public PostgreSQLLargeObjectStream(PostgreSQLClientTools.RunningTool restore, Action<string?> completed)
    {
        m_restore = restore;
        m_completed = completed;
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        m_written += buffer.Length;
        try
        {
            await m_restore.Input.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            m_failed = true;
            var (exitCode, errorOutput) = await m_restore.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            throw new IOException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.PgRestore, exitCode, errorOutput));
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await CompleteAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CompleteAsync().GetAwaiter().GetResult();

        base.Dispose(disposing);
    }

    private async Task CompleteAsync()
    {
        if (m_disposed)
            return;
        m_disposed = true;

        using (m_restore)
        {
            // A write that failed was reported where it happened
            if (m_failed)
                return;

            try
            {
                m_restore.Input.Close();
            }
            catch (IOException)
            {
            }

            // pg_restore runs in one transaction, so an archive that was cut short restores nothing
            var (exitCode, errorOutput) = await m_restore.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            m_completed(exitCode == 0 ? null : Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.PgRestore, exitCode, errorOutput));
        }
    }
}
