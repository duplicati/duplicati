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

using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// An entry in the table of contents of a pg_dump archive
/// </summary>
/// <param name="DumpId">The id of the entry within this dump; not stable between dumps</param>
/// <param name="HasData">True when the archive has a data block for the entry</param>
/// <param name="Oid">The catalog object id, as text</param>
/// <param name="Tag">The object name</param>
/// <param name="Description">The kind of entry, such as TABLE DATA</param>
/// <param name="Namespace">The schema of the object, or null</param>
/// <param name="CopyStatement">The COPY statement that loads the data, or null</param>
public sealed record PostgreSQLArchiveEntry(int DumpId, bool HasData, string? Oid, string? Tag, string? Description, string? Namespace, string? CopyStatement);

/// <summary>
/// Reads the custom format that pg_dump writes, from a stream that can only be read forward.
/// The archive is a header and a table of contents, followed by one data block for each table
/// and for each group of large objects. The format is internal to PostgreSQL and versioned in
/// the header, so only versions that are tested are accepted.
/// </summary>
public sealed class PostgreSQLArchiveReader
{
    /// <summary>The description of a table of contents entry that holds the rows of a table</summary>
    public const string TableDataDescription = "TABLE DATA";
    /// <summary>The description of a table of contents entry that holds large objects</summary>
    public const string LargeObjectsDescription = "BLOBS";

    /// <summary>The block type of table data</summary>
    public const byte BlockTypeData = 1;
    /// <summary>The block type of large objects</summary>
    public const byte BlockTypeLargeObjects = 3;

    /// <summary>The major version of every archive format that is accepted</summary>
    private const int FormatMajor = 1;
    /// <summary>The minor version PostgreSQL 14 and 15 write</summary>
    private const int FormatMinor14 = 14;
    /// <summary>The minor version PostgreSQL 16 writes: the compression is a method byte, where it was a level before</summary>
    private const int FormatMinorCompressionMethod = 15;
    /// <summary>The minor version PostgreSQL 17 and 18 write: each entry has a relation kind</summary>
    private const int FormatMinorRelationKind = 16;

    /// <summary>
    /// The archive versions that are accepted, as major and minor
    /// </summary>
    public static readonly (int Major, int Minor)[] SupportedVersions =
    [
        (FormatMajor, FormatMinor14),
        (FormatMajor, FormatMinorCompressionMethod),
        (FormatMajor, FormatMinorRelationKind)
    ];

    /// <summary>The bytes every archive starts with</summary>
    private static readonly byte[] Magic = "PGDMP"u8.ToArray();
    /// <summary>The size of an integer in the archive, after its sign byte</summary>
    private const int IntegerSize = 4;
    /// <summary>The size of a file offset in the archive, after its flag byte</summary>
    private const int OffsetSize = 8;
    /// <summary>The format byte of the custom format</summary>
    private const byte CustomFormat = 1;
    /// <summary>The compression method or level of an uncompressed archive</summary>
    private const int NoCompression = 0;
    /// <summary>The number of integers the creation time is written as</summary>
    private const int TimestampFields = 7;
    /// <summary>The size of the read buffer</summary>
    private const int BufferSize = 256 * 1024;

    private readonly Stream m_stream;
    private readonly byte[] m_buffer = new byte[BufferSize];
    private int m_position;
    private int m_length;
    private MemoryStream? m_recorded = new();
    private int m_chunkRemaining;
    private bool m_inBlock;

    /// <summary>The header and table of contents, exactly as pg_dump wrote them</summary>
    public byte[] HeaderAndTableOfContents { get; private set; } = [];
    /// <summary>The entries of the table of contents, in archive order</summary>
    public IReadOnlyList<PostgreSQLArchiveEntry> Entries { get; private set; } = [];
    /// <summary>The archive format version, as in 1.16.0</summary>
    public string ArchiveVersion { get; private set; } = string.Empty;
    /// <summary>The server version the dump was taken from, as pg_dump recorded it</summary>
    public string ServerVersion { get; private set; } = string.Empty;
    /// <summary>The version of pg_dump that wrote the archive</summary>
    public string ClientVersion { get; private set; } = string.Empty;
    /// <summary>The bytes of the last block header that was read</summary>
    public byte[] LastBlockHeader { get; private set; } = [];

    private PostgreSQLArchiveReader(Stream stream)
    {
        m_stream = stream;
    }

    /// <summary>
    /// Reads the header and the table of contents from the start of an archive.
    /// Fails before any data is read if the archive is not one of the supported versions.
    /// </summary>
    /// <param name="stream">The archive stream</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The reader, positioned at the first data block</returns>
    public static async Task<PostgreSQLArchiveReader> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        var reader = new PostgreSQLArchiveReader(stream);
        await reader.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
        return reader;
    }

    private async Task ReadHeaderAsync(CancellationToken cancellationToken)
    {
        var magic = await ReadBytesAsync(Magic.Length, cancellationToken).ConfigureAwait(false);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.UnexpectedArchive("it does not start with the archive signature"), "PostgreSQLUnexpectedArchive");

        var version = await ReadBytesAsync(3, cancellationToken).ConfigureAwait(false);
        ArchiveVersion = $"{version[0]}.{version[1]}.{version[2]}";
        if (!SupportedVersions.Contains((version[0], version[1])))
            throw new UserInformationException(
                Strings.PostgreSQLSourceProvider.UnsupportedArchiveVersion(ArchiveVersion, string.Join(", ", SupportedVersions.Select(x => $"{x.Major}.{x.Minor}"))),
                "PostgreSQLUnsupportedArchiveVersion");

        var minor = version[1];

        var sizes = await ReadBytesAsync(3, cancellationToken).ConfigureAwait(false);
        if (sizes[0] != IntegerSize || sizes[1] != OffsetSize)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.UnexpectedArchive($"integer size {sizes[0]} and offset size {sizes[1]}"), "PostgreSQLUnexpectedArchive");
        if (sizes[2] != CustomFormat)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.UnexpectedArchive($"format {sizes[2]} is not the custom format"), "PostgreSQLUnexpectedArchive");

        // The compression is a method byte, and in the older formats a level written as an integer
        var compression = minor >= FormatMinorCompressionMethod
            ? (await ReadBytesAsync(1, cancellationToken).ConfigureAwait(false))[0]
            : await ReadIntAsync(cancellationToken).ConfigureAwait(false);
        if (compression != NoCompression)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.UnexpectedArchive($"it is compressed with method or level {compression}"), "PostgreSQLUnexpectedArchive");

        for (var i = 0; i < TimestampFields; i++)
            await ReadIntAsync(cancellationToken).ConfigureAwait(false);

        // The database name, then the server and the pg_dump versions
        await ReadStringAsync(cancellationToken).ConfigureAwait(false);
        ServerVersion = await ReadStringAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        ClientVersion = await ReadStringAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

        var count = await ReadIntAsync(cancellationToken).ConfigureAwait(false);
        if (count < 0)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.UnexpectedArchive($"the table of contents has {count} entries"), "PostgreSQLUnexpectedArchive");

        var entries = new List<PostgreSQLArchiveEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var dumpId = await ReadIntAsync(cancellationToken).ConfigureAwait(false);
            var hasData = await ReadIntAsync(cancellationToken).ConfigureAwait(false) != 0;
            // The catalog table the object is in
            await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            var oid = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            var tag = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            var description = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            // The section: pre-data, data or post-data
            await ReadIntAsync(cancellationToken).ConfigureAwait(false);
            // The definition and the drop statement
            await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            var copyStatement = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            var schema = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            // The tablespace and the table access method
            await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            // The relation kind
            if (minor >= FormatMinorRelationKind)
                await ReadIntAsync(cancellationToken).ConfigureAwait(false);
            // The owner, and the unused "with oids" marker
            await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            // The dependencies, ended by a null string
            while (await ReadStringAsync(cancellationToken).ConfigureAwait(false) != null)
            {
            }
            // The position of the data block, which is not known in a piped archive
            await ReadBytesAsync(1 + OffsetSize, cancellationToken).ConfigureAwait(false);

            entries.Add(new PostgreSQLArchiveEntry(dumpId, hasData, oid, tag, description, schema, copyStatement));
        }

        Entries = entries;
        HeaderAndTableOfContents = m_recorded!.ToArray();
        m_recorded = null;
    }

    /// <summary>
    /// Reads the header of the next data block
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The block type and the dump id it belongs to, or null at the end of the archive</returns>
    public async Task<(byte Type, int DumpId)?> ReadBlockHeaderAsync(CancellationToken cancellationToken)
    {
        if (m_inBlock)
            throw new InvalidOperationException("The previous block has not been read to its end");

        if (m_position == m_length && !await FillAsync(cancellationToken).ConfigureAwait(false))
            return null;

        await EnsureAsync(2 + IntegerSize, cancellationToken).ConfigureAwait(false);
        LastBlockHeader = m_buffer.AsSpan(m_position, 2 + IntegerSize).ToArray();
        var type = m_buffer[m_position++];
        var dumpId = await ReadIntAsync(cancellationToken).ConfigureAwait(false);
        if (type != BlockTypeData && type != BlockTypeLargeObjects)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.UnexpectedArchive($"block type {type}"), "PostgreSQLUnexpectedArchive");

        m_inBlock = type == BlockTypeData;
        m_chunkRemaining = 0;
        return (type, dumpId);
    }

    /// <summary>
    /// Reads data from the current table data block, without the framing of the archive
    /// </summary>
    /// <param name="buffer">The buffer to read into</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The number of bytes read, or zero at the end of the block</returns>
    public async ValueTask<int> ReadBlockDataAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!m_inBlock || buffer.Length == 0)
            return 0;

        if (m_chunkRemaining == 0)
        {
            m_chunkRemaining = await ReadIntAsync(cancellationToken).ConfigureAwait(false);
            if (m_chunkRemaining < 0)
                throw new UserInformationException(Strings.PostgreSQLSourceProvider.UnexpectedArchive($"a data chunk of length {m_chunkRemaining}"), "PostgreSQLUnexpectedArchive");
            if (m_chunkRemaining == 0)
            {
                m_inBlock = false;
                return 0;
            }
        }

        if (m_position == m_length && !await FillAsync(cancellationToken).ConfigureAwait(false))
            throw new EndOfStreamException();

        var count = Math.Min(Math.Min(m_length - m_position, m_chunkRemaining), buffer.Length);
        m_buffer.AsSpan(m_position, count).CopyTo(buffer.Span);
        m_position += count;
        m_chunkRemaining -= count;
        return count;
    }

    /// <summary>
    /// Reads and discards the rest of the current table data block
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    public async Task DiscardBlockAsync(CancellationToken cancellationToken)
    {
        var scratch = new byte[64 * 1024];
        while (await ReadBlockDataAsync(scratch, cancellationToken).ConfigureAwait(false) > 0)
        {
        }
    }

    /// <summary>
    /// Reads the archive as it is, without interpreting it. Used for the large objects,
    /// which are the last part of the archive.
    /// </summary>
    /// <param name="buffer">The buffer to read into</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The number of bytes read, or zero at the end of the archive</returns>
    public async ValueTask<int> ReadRawAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0)
            return 0;
        if (m_position == m_length && !await FillAsync(cancellationToken).ConfigureAwait(false))
            return 0;

        var count = Math.Min(m_length - m_position, buffer.Length);
        m_buffer.AsSpan(m_position, count).CopyTo(buffer.Span);
        m_position += count;
        return count;
    }

    /// <summary>
    /// Reads more of the stream into the buffer
    /// </summary>
    /// <returns>False at the end of the stream</returns>
    private async ValueTask<bool> FillAsync(CancellationToken cancellationToken)
    {
        if (m_position == m_length)
            m_position = m_length = 0;
        else if (m_position > 0)
        {
            Buffer.BlockCopy(m_buffer, m_position, m_buffer, 0, m_length - m_position);
            m_length -= m_position;
            m_position = 0;
        }

        var read = await m_stream.ReadAsync(m_buffer.AsMemory(m_length), cancellationToken).ConfigureAwait(false);
        m_length += read;
        return read > 0;
    }

    /// <summary>
    /// Makes sure a number of bytes are in the buffer
    /// </summary>
    private async ValueTask EnsureAsync(int count, CancellationToken cancellationToken)
    {
        while (m_length - m_position < count)
            if (!await FillAsync(cancellationToken).ConfigureAwait(false))
                throw new EndOfStreamException();
    }

    /// <summary>
    /// Takes a number of bytes out of the buffer, recording them while the header is read
    /// </summary>
    private ReadOnlySpan<byte> Consume(int count)
    {
        var span = m_buffer.AsSpan(m_position, count);
        m_recorded?.Write(span);
        m_position += count;
        return span;
    }

    /// <summary>
    /// Reads an integer: a sign byte followed by the value, least significant byte first
    /// </summary>
    private async ValueTask<int> ReadIntAsync(CancellationToken cancellationToken)
    {
        if (m_length - m_position < 1 + IntegerSize)
            await EnsureAsync(1 + IntegerSize, cancellationToken).ConfigureAwait(false);

        var span = Consume(1 + IntegerSize);
        var value = span[1] | (span[2] << 8) | (span[3] << 16) | (span[4] << 24);
        return span[0] != 0 ? -value : value;
    }

    /// <summary>
    /// Reads a string: its length as an integer, where a negative length is a null string, then the text
    /// </summary>
    private async ValueTask<string?> ReadStringAsync(CancellationToken cancellationToken)
    {
        var length = await ReadIntAsync(cancellationToken).ConfigureAwait(false);
        if (length < 0)
            return null;

        return Encoding.UTF8.GetString(await ReadBytesAsync(length, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Reads a number of bytes, which may be more than the buffer holds
    /// </summary>
    private async ValueTask<byte[]> ReadBytesAsync(int count, CancellationToken cancellationToken)
    {
        var result = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            if (m_position == m_length && !await FillAsync(cancellationToken).ConfigureAwait(false))
                throw new EndOfStreamException();

            var part = Math.Min(m_length - m_position, count - offset);
            Consume(part).CopyTo(result.AsSpan(offset));
            offset += part;
        }

        return result;
    }
}
