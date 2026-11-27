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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Compression.ZipCompression;

/// <summary>
/// A read-only ZIP reader that keeps only a compact index of the entries.
/// </summary>
/// <remarks>
/// The ZIP libraries keep an object, a name string and a name buffer for every entry, about
/// 500 bytes per entry. A restore keeps every volume it still needs open, and a block volume
/// has an entry for every block, so a large restore held gigabytes in these catalogs (#7538).
/// This reader keeps the names in one buffer and the entries in one array, under 100 bytes per
/// entry, and reads the data straight from the file. It reads only stored and deflated entries
/// without encryption, which is what Duplicati writes by default. For anything else
/// <see cref="TryOpen"/> returns <c>null</c>, and the caller uses the regular reader.
/// </remarks>
public sealed class CentralDirectoryZipReader : ICompression
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<CentralDirectoryZipReader>();

    private const uint LOCAL_HEADER_SIGNATURE = 0x04034b50;
    private const uint CENTRAL_HEADER_SIGNATURE = 0x02014b50;
    private const uint END_OF_CENTRAL_DIRECTORY_SIGNATURE = 0x06054b50;
    private const uint ZIP64_END_OF_CENTRAL_DIRECTORY_SIGNATURE = 0x06064b50;
    private const uint ZIP64_LOCATOR_SIGNATURE = 0x07064b50;
    private const int END_OF_CENTRAL_DIRECTORY_SIZE = 22;
    private const int ZIP64_LOCATOR_SIZE = 20;
    private const int CENTRAL_HEADER_SIZE = 46;
    private const int LOCAL_HEADER_SIZE = 30;
    private const ushort ZIP64_EXTRA_ID = 0x0001;
    private const ushort METHOD_STORED = 0;
    private const ushort METHOD_DEFLATE = 8;

    /// <summary>
    /// An entry in the archive. The name is stored in <see cref="m_names"/>.
    /// </summary>
    private readonly record struct Entry(long HeaderOffset, long CompressedSize, long Size, int NameStart, ushort NameLength, ushort Method);

    /// <summary>
    /// The stream with the archive, owned by this reader.
    /// </summary>
    private readonly Stream m_stream;

    /// <summary>
    /// The names of all entries, with '\' replaced by '/', one after another.
    /// </summary>
    private readonly byte[] m_names;

    /// <summary>
    /// The entries, sorted by name, with one entry for each name.
    /// </summary>
    private readonly Entry[] m_entries;

    /// <summary>
    /// The options to open the regular reader with, if an entry cannot be read.
    /// </summary>
    private readonly IDictionary<string, string?> m_options;

    /// <summary>
    /// The regular reader, used once an entry could not be read by this reader.
    /// </summary>
    private FileArchiveZip? m_fallback;

    private CentralDirectoryZipReader(Stream stream, byte[] names, Entry[] entries, IDictionary<string, string?> options)
    {
        m_stream = stream;
        m_names = names;
        m_entries = entries;
        m_options = options;
    }

    /// <summary>
    /// Opens a ZIP file with a compact index.
    /// </summary>
    /// <param name="path">The path to the ZIP file.</param>
    /// <param name="options">The options, used if the regular reader is needed later.</param>
    /// <returns>The reader, or <c>null</c> if the file uses something this reader does not read.</returns>
    public static CentralDirectoryZipReader? TryOpen(string path, IDictionary<string, string?> options)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var reader = TryOpen(stream, options);
            if (reader == null)
                stream.Dispose();
            return reader;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a ZIP stream with a compact index. The reader takes ownership of the stream if it returns one.
    /// </summary>
    /// <param name="stream">The seekable stream with the ZIP data.</param>
    /// <param name="options">The options, used if the regular reader is needed later.</param>
    /// <returns>The reader, or <c>null</c> if the stream uses something this reader does not read.</returns>
    internal static CentralDirectoryZipReader? TryOpen(Stream stream, IDictionary<string, string?> options)
    {
        try
        {
            if (!TryReadIndex(stream, out var names, out var entries))
                return null;
            return new CentralDirectoryZipReader(stream, names, entries, options);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException or OverflowException)
        {
            Log.WriteVerboseMessage(LOGTAG, "CentralDirectoryReadFailed", ex, "Could not read the central directory, using the regular ZIP reader");
            return null;
        }
    }

    /// <summary>
    /// Reads the central directory into a compact index.
    /// </summary>
    private static bool TryReadIndex(Stream stream, out byte[] names, out Entry[] entries)
    {
        names = [];
        entries = [];

        var length = stream.Length;
        if (length < END_OF_CENTRAL_DIRECTORY_SIZE)
            return false;

        // The end record is followed by a comment of at most 65535 bytes
        var tailLength = (int)Math.Min(length, END_OF_CENTRAL_DIRECTORY_SIZE + ushort.MaxValue + ZIP64_LOCATOR_SIZE);
        var tail = new byte[tailLength];
        stream.Position = length - tailLength;
        stream.ReadExactly(tail);

        var eocd = -1;
        for (var i = tailLength - END_OF_CENTRAL_DIRECTORY_SIZE; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == END_OF_CENTRAL_DIRECTORY_SIGNATURE
                && i + END_OF_CENTRAL_DIRECTORY_SIZE + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) == tailLength)
            {
                eocd = i;
                break;
            }
        }
        if (eocd < 0)
            return false;

        // Multi-disk archives are not read
        if (BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 4)) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 6)) != 0)
            return false;

        long directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16));
        // The central directory ends where the records after it start
        var directoryEnd = length - tailLength + eocd;

        if (eocd >= ZIP64_LOCATOR_SIZE && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd - ZIP64_LOCATOR_SIZE)) == ZIP64_LOCATOR_SIGNATURE)
        {
            var zip64RecordOffset = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(eocd - ZIP64_LOCATOR_SIZE + 8));
            var record = new byte[56];
            stream.Position = zip64RecordOffset;
            stream.ReadExactly(record);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) != ZIP64_END_OF_CENTRAL_DIRECTORY_SIGNATURE)
                return false;
            directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(48));
            directoryEnd = zip64RecordOffset;
        }
        else if (directoryOffset == uint.MaxValue)
        {
            return false;
        }

        if (directoryOffset < 0 || directoryOffset > directoryEnd)
            return false;

        // The entry count in the end record is not used, as older versions wrote a wrong one
        // for more than 65535 entries (#6251). The headers are read until the directory ends.
        var directory = new byte[checked((int)(directoryEnd - directoryOffset))];
        stream.Position = directoryOffset;
        stream.ReadExactly(directory);

        var nameBuffer = new MemoryStream();
        var list = new List<Entry>();
        var pos = 0;
        while (pos + CENTRAL_HEADER_SIZE <= directory.Length && BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(pos)) == CENTRAL_HEADER_SIGNATURE)
        {
            var header = directory.AsSpan(pos);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
            var method = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
            long compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            long headerOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[42..]);

            var next = pos + CENTRAL_HEADER_SIZE + nameLength + extraLength + commentLength;
            if (next > directory.Length)
                return false;

            // Encrypted entries and other compression methods are left to the regular reader
            if ((flags & 1) != 0 || (method != METHOD_STORED && method != METHOD_DEFLATE))
                return false;

            if (size == uint.MaxValue || compressedSize == uint.MaxValue || headerOffset == uint.MaxValue)
            {
                var extra = header.Slice(CENTRAL_HEADER_SIZE + nameLength, extraLength);
                if (!TryReadZip64Extra(extra, ref size, ref compressedSize, ref headerOffset))
                    return false;
            }

            var name = header.Slice(CENTRAL_HEADER_SIZE, nameLength).ToArray();
            for (var i = 0; i < name.Length; i++)
                if (name[i] == (byte)'\\')
                    name[i] = (byte)'/';

            list.Add(new Entry(headerOffset, compressedSize, size, checked((int)nameBuffer.Length), nameLength, method));
            nameBuffer.Write(name);
            pos = next;
        }

        names = nameBuffer.ToArray();

        // Sort by name, and keep the last of entries with the same name, as the regular reader does
        var namesLocal = names;
        var order = Enumerable.Range(0, list.Count).ToArray();
        Array.Sort(order, (a, b) =>
        {
            var c = NameOf(namesLocal, list[a]).SequenceCompareTo(NameOf(namesLocal, list[b]));
            return c != 0 ? c : a.CompareTo(b);
        });

        var result = new List<Entry>(list.Count);
        for (var i = 0; i < order.Length; i++)
        {
            if (i + 1 < order.Length && NameOf(namesLocal, list[order[i]]).SequenceEqual(NameOf(namesLocal, list[order[i + 1]])))
                continue;
            result.Add(list[order[i]]);
        }

        entries = result.ToArray();
        return true;
    }

    /// <summary>
    /// Reads the 64-bit values from the Zip64 extra field of a central header.
    /// </summary>
    private static bool TryReadZip64Extra(ReadOnlySpan<byte> extra, ref long size, ref long compressedSize, ref long headerOffset)
    {
        while (extra.Length >= 4)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            var fieldLength = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);
            if (4 + fieldLength > extra.Length)
                return false;

            if (id == ZIP64_EXTRA_ID)
            {
                // Only the values that are all ones in the header are in the field, in this order
                var field = extra.Slice(4, fieldLength);
                var p = 0;
                if (size == uint.MaxValue)
                {
                    if (p + 8 > field.Length) return false;
                    size = BinaryPrimitives.ReadInt64LittleEndian(field[p..]);
                    p += 8;
                }
                if (compressedSize == uint.MaxValue)
                {
                    if (p + 8 > field.Length) return false;
                    compressedSize = BinaryPrimitives.ReadInt64LittleEndian(field[p..]);
                    p += 8;
                }
                if (headerOffset == uint.MaxValue)
                {
                    if (p + 8 > field.Length) return false;
                    headerOffset = BinaryPrimitives.ReadInt64LittleEndian(field[p..]);
                }
                return size >= 0 && compressedSize >= 0 && headerOffset >= 0;
            }

            extra = extra[(4 + fieldLength)..];
        }

        return false;
    }

    private static ReadOnlySpan<byte> NameOf(byte[] names, Entry entry)
        => names.AsSpan(entry.NameStart, entry.NameLength);

    private string NameOf(Entry entry)
        => Encoding.UTF8.GetString(m_names, entry.NameStart, entry.NameLength);

    /// <summary>
    /// Finds the entry with the given name.
    /// </summary>
    private int Find(string file)
    {
        var key = Encoding.UTF8.GetBytes(file.Replace('\\', '/'));
        int lo = 0, hi = m_entries.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var c = NameOf(m_names, m_entries[mid]).SequenceCompareTo(key);
            if (c == 0)
                return mid;
            if (c < 0)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return -1;
    }

    /// <inheritdoc />
    public Stream? OpenRead(string file)
    {
        if (m_fallback != null)
            return m_fallback.OpenRead(file);

        var index = Find(file);
        if (index < 0)
            return null;

        var entry = m_entries[index];
        try
        {
            var header = new byte[LOCAL_HEADER_SIZE];
            m_stream.Position = entry.HeaderOffset;
            m_stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LOCAL_HEADER_SIGNATURE)
                throw new InvalidDataException($"No local header for the entry {file}");

            var dataOffset = entry.HeaderOffset + LOCAL_HEADER_SIZE
                + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26))
                + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
            if (dataOffset + entry.CompressedSize > m_stream.Length)
                throw new InvalidDataException($"The entry {file} goes past the end of the archive");

            Stream data = new SubStream(m_stream, dataOffset, entry.CompressedSize);
            return entry.Method == METHOD_DEFLATE
                ? new DeflateStream(data, CompressionMode.Decompress)
                : data;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            // The regular reader has a fallback of its own for archives it cannot read either
            Log.WriteWarningMessage(LOGTAG, "CentralDirectoryEntryReadFailed", ex, "Could not read the entry {0} with the compact reader, using the regular ZIP reader", file);
            m_stream.Position = 0;
            m_fallback = new FileArchiveZip(m_stream, ArchiveMode.Read, m_options);
            return m_fallback.OpenRead(file);
        }
    }

    /// <inheritdoc />
    public bool FileExists(string file)
        => m_fallback?.FileExists(file) ?? Find(file) >= 0;

    /// <inheritdoc />
    public string[] ListFiles(string prefix)
        => ListFilesWithSize(prefix).Select(x => x.Key).ToArray();

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string prefix)
    {
        if (m_fallback != null)
            return m_fallback.ListFilesWithSize(prefix);

        var normalized = prefix?.Replace('\\', '/');
        return m_entries
            .Select(e => new KeyValuePair<string, long>(NameOf(e), e.Size))
            .Where(x => string.IsNullOrWhiteSpace(normalized) || x.Key.StartsWith(normalized, StringComparison.Ordinal))
            .ToArray();
    }

    /// <inheritdoc />
    public DateTime GetLastWriteTime(string file)
    {
        if (m_fallback != null)
            return m_fallback.GetLastWriteTime(file);

        var index = Find(file);
        if (index < 0)
            return new DateTime(0, DateTimeKind.Utc);

        var header = new byte[LOCAL_HEADER_SIZE];
        m_stream.Position = m_entries[index].HeaderOffset;
        m_stream.ReadExactly(header);
        var time = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10));
        var date = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12));
        try
        {
            return new DateTime(1980 + (date >> 9), (date >> 5) & 0xf, date & 0x1f, time >> 11, (time >> 5) & 0x3f, (time & 0x1f) * 2);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new DateTime(0, DateTimeKind.Utc);
        }
    }

    /// <inheritdoc />
    public long Size => m_stream.Length;

    /// <inheritdoc />
    public long FlushBufferSize => 0;

    /// <inheritdoc />
    public string FilenameExtension => "zip";

    /// <inheritdoc />
    public string DisplayName => Strings.FileArchiveZip.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.FileArchiveZip.Description;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => [];

    /// <inheritdoc />
    public Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
        => throw new NotSupportedException("The compact ZIP reader cannot write");

    /// <inheritdoc />
    public void Dispose()
    {
        m_fallback?.Dispose();
        m_stream.Dispose();
    }

    /// <summary>
    /// A read-only view of a part of a stream. It seeks before every read, so several views can share the stream, one at a time.
    /// </summary>
    private sealed class SubStream(Stream baseStream, long start, long length) : Stream
    {
        private long m_position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => m_position;
            set => m_position = Math.Clamp(value, 0, length);
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var remaining = length - m_position;
            if (remaining <= 0)
                return 0;
            if (buffer.Length > remaining)
                buffer = buffer[..(int)remaining];

            baseStream.Position = start + m_position;
            var read = baseStream.Read(buffer);
            m_position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => m_position + offset,
                _ => length + offset
            };
            return m_position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
