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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using Duplicati.StreamUtil;

namespace Duplicati.Library.Compression.TarZstdCompression;

/// <summary>
/// Abstract base class for Tar-based compression implementations.
/// File format: [Compressed Stream] containing [Tar Archive] + [.eof-header with dictionary]
/// </summary>
public abstract class FileArchiveTarBased : ICompression
{
    private static readonly string LOGTAG = Log.LogTagFromType<FileArchiveTarBased>();

    // Constant ustar header fields
    // Mode: 0000644 (8 bytes at offset 100)
    private static readonly byte[] UstarModeBytes = "0000644 "u8.ToArray();
    // UID: 0 (8 bytes at offset 108)
    private static readonly byte[] UstarUidBytes = "0000000 "u8.ToArray();
    // GID: 0 (8 bytes at offset 116)
    private static readonly byte[] UstarGidBytes = "0000000 "u8.ToArray();
    // Magic: "ustar\0" (6 bytes at offset 257)
    private static readonly byte[] UstarMagicBytes = "ustar\0"u8.ToArray();
    // Version: "00" (2 bytes at offset 263)
    private static readonly byte[] UstarVersionBytes = "00"u8.ToArray();

    private readonly ArchiveMode m_mode;
    private readonly Stream? m_inputStream;
    private readonly Stream? m_outputStream;
    private readonly string? m_tempFilePath;
    private FileStream? m_tempFileStream;
    private readonly int m_compressionLevel;
    private readonly Dictionary<string, FileEntry> m_entries;

    // Write mode fields
    private Stream? m_compressorStream;
    private readonly Dictionary<string, SerializableEntry> m_writeEntries;
    private long m_tarPosition;
    private int m_entryCount;
    private PendingEntryStream? m_currentStream;
    private readonly bool m_useMemoryBuffer;
    private long m_bytesWritten;
    private bool m_disposed;

    /// <summary>
    /// The filename extension for this compression format
    /// </summary>
    public abstract string FilenameExtension { get; }

    /// <summary>
    /// The display name for this compression format
    /// </summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// The description for this compression format
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// The option name for compression level
    /// </summary>
    protected abstract string CompressionLevelOption { get; }

    /// <summary>
    /// The default compression level
    /// </summary>
    protected abstract int DefaultCompressionLevel { get; }

    /// <summary>
    /// The minimum compression level
    /// </summary>
    protected abstract int MinCompressionLevel { get; }

    /// <summary>
    /// The maximum compression level
    /// </summary>
    protected abstract int MaxCompressionLevel { get; }

    /// <summary>
    /// The option name for using memory buffer instead of temp files
    /// </summary>
    protected abstract string MemoryBufferOption { get; }

    /// <summary>
    /// Creates a decompressor stream wrapper
    /// </summary>
    protected abstract Stream CreateDecompressorStream(Stream inputStream);

    /// <summary>
    /// Creates a compressor stream wrapper
    /// </summary>
    protected abstract Stream CreateCompressorStream(Stream outputStream, int compressionLevel);

    /// <summary>
    /// Default constructor for module discovery
    /// </summary>
    protected FileArchiveTarBased()
    {
        m_mode = ArchiveMode.Read;
        m_compressionLevel = DefaultCompressionLevel;
        m_entries = null!;
        m_writeEntries = null!;
    }

    protected FileArchiveTarBased(Stream stream, ArchiveMode mode, IReadOnlyDictionary<string, string?> options)
    {
        m_mode = mode;
        m_entries = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
        m_writeEntries = [];

        m_compressionLevel = Math.Clamp(
            Utility.Utility.ParseIntOption(options, CompressionLevelOption, DefaultCompressionLevel), MinCompressionLevel, MaxCompressionLevel);

        if (mode == ArchiveMode.Read)
        {
            m_inputStream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (!m_inputStream.CanRead)
                throw new ArgumentException("Stream must be readable", nameof(stream));

            m_tempFilePath = Path.GetTempFileName();
            m_tempFileStream = new FileStream(m_tempFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose);

            DecompressToTempFile();

            m_tempFileStream.Position = 0;
            if (!TryReadEofHeader(m_tempFileStream, out var entries))
            {
                m_tempFileStream.Position = 0;
                entries = BuildDictionaryByScanning(m_tempFileStream);
            }

            foreach (var entry in entries)
                m_entries[entry.Key] = entry.Value;
        }
        else
        {
            m_outputStream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (!m_outputStream.CanWrite)
                throw new ArgumentException("Stream must be writable", nameof(stream));

            m_useMemoryBuffer = Utility.Utility.ParseBoolOption(options, MemoryBufferOption);
            m_compressorStream = CreateCompressorStream(m_outputStream, m_compressionLevel);
            m_tarPosition = 0;
            m_entryCount = 0;
            m_bytesWritten = 0;
        }
    }

    private void DecompressToTempFile()
    {
        if (m_inputStream == null || m_tempFileStream == null)
            return;

        try
        {
            using var decompressor = CreateDecompressorStream(m_inputStream);
            decompressor.CopyTo(m_tempFileStream);
            m_tempFileStream.Flush();
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Failed to decompress stream", ex);
        }
    }

    public abstract IList<ICommandLineArgument> SupportedCommands { get; }

    public long Size => m_mode switch
    {
        ArchiveMode.Read => m_inputStream?.Length ?? 0,
        ArchiveMode.Write => m_bytesWritten, // Track raw bytes written (assuming zero compression)
        _ => 0
    };

    public long FlushBufferSize
    {
        get
        {
            if (m_mode != ArchiveMode.Write)
                return 0;

            // Current file being written + its header (if any)
            long total = m_currentStream?.Length + 512 ?? 0;

            // EOF header estimate: header (512) + JSON (~60 bytes per entry) + padding + trailer (14)
            // JSON format: {"path":{"Offset":N,"Size":N,"LastWriteTime":N}}
            // Rough estimate: 60 chars per entry
            long eofJsonSize = m_entryCount * 60 + 100;
            long eofHeaderSize = 512 + ((eofJsonSize + 511) / 512 * 512) + 14;
            eofHeaderSize = (eofHeaderSize + 511) / 512 * 512;
            total += eofHeaderSize;

            return total;
        }
    }

    public string[] ListFiles(string? prefix)
    {
        if (m_mode != ArchiveMode.Read)
            throw new InvalidOperationException("Cannot read while writing");

        var files = m_entries.Keys;
        if (string.IsNullOrEmpty(prefix))
            return files.ToArray();

        return files
            .Where(f => f.StartsWith(prefix, StringComparison.Ordinal) ||
                       f.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
    }

    public IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string? prefix)
    {
        if (m_mode != ArchiveMode.Read)
            throw new InvalidOperationException("Cannot read while writing");

        foreach (var entry in m_entries)
        {
            if (string.IsNullOrEmpty(prefix) ||
                entry.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                entry.Key.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal))
            {
                yield return new KeyValuePair<string, long>(entry.Key, entry.Value.Size);
            }
        }
    }

    public Stream? OpenRead(string file)
    {
        if (m_mode != ArchiveMode.Read)
            throw new InvalidOperationException("Cannot read while writing");

        var normalizedFile = file.Replace('\\', '/');

        if (!m_entries.TryGetValue(normalizedFile, out var entry))
        {
            if (file != normalizedFile)
                m_entries.TryGetValue(file, out entry);
        }

        if (entry == null)
            return null;

        if (m_tempFileStream == null)
            return null;

        return new ReadLimitLengthStream(m_tempFileStream, entry.Offset, entry.Size);
    }

    public DateTime GetLastWriteTime(string file)
    {
        if (m_mode != ArchiveMode.Read)
            throw new InvalidOperationException("Cannot read while writing");

        var normalizedFile = file.Replace('\\', '/');
        if (m_entries.TryGetValue(normalizedFile, out var entry))
            return entry.LastWriteTime;

        if (file != normalizedFile && m_entries.TryGetValue(file, out entry))
            return entry.LastWriteTime;

        throw new FileNotFoundException($"File not found: {file}");
    }

    public bool FileExists(string file)
    {
        if (m_mode != ArchiveMode.Read)
            throw new InvalidOperationException("Cannot read while writing");

        var normalizedFile = file.Replace('\\', '/');
        return m_entries.ContainsKey(normalizedFile) ||
               (file != normalizedFile && m_entries.ContainsKey(file));
    }

    public Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
    {
        if (m_mode != ArchiveMode.Write)
            throw new InvalidOperationException("Cannot write while reading");

        var normalizedFile = file.Replace('\\', '/');

        if (m_writeEntries.ContainsKey(normalizedFile))
            throw new InvalidOperationException($"File already exists: {normalizedFile}");

        if (m_currentStream != null)
            throw new InvalidOperationException("Cannot create a new file while another file is still open");

        m_currentStream = new PendingEntryStream(this, normalizedFile, lastWrite, m_useMemoryBuffer);
        return m_currentStream;
    }

    public void Dispose()
    {
        if (m_disposed)
            return;

        try
        {
            if (m_mode == ArchiveMode.Write)
            {
                WriteEofHeader();
                m_compressorStream?.Flush();
                m_compressorStream?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "DisposeError", ex, "Error during disposal");
        }
        finally
        {
            m_tempFileStream?.Dispose();

            if (m_tempFilePath != null && File.Exists(m_tempFilePath))
            {
                try { File.Delete(m_tempFilePath); } catch { }
            }

            m_disposed = true;
        }
    }

    private void WriteEntryToTar(string name, DateTime lastWriteTime, Stream contentStream, long size)
    {
        if (m_compressorStream == null)
            throw new InvalidOperationException("Not in write mode");

        // Record content offset (after the 512-byte header)
        var contentOffset = m_tarPosition + 512;

        // Write tar header (512 bytes)
        WriteUstarHeader(m_compressorStream, name, size, lastWriteTime);
        m_bytesWritten += 512;

        // Write content from stream
        contentStream.CopyTo(m_compressorStream);
        m_bytesWritten += size;

        // Pad to 512-byte boundary
        var padding = 512 - (int)(size % 512);
        if (padding != 512)
        {
            m_compressorStream.Write(new byte[padding], 0, padding);
            m_tarPosition += padding;
            m_bytesWritten += padding;
        }

        m_tarPosition += 512 + size;
        m_currentStream = null;

        // Track entry for EOF header
        m_writeEntries[name] = new SerializableEntry(
            contentOffset,
            size,
            Utility.Utility.NormalizeDateTimeToEpochSeconds(lastWriteTime)
        );
        m_entryCount++;
    }

    private void WriteEofHeader()
    {
        if (m_compressorStream == null || m_writeEntries.Count == 0)
            return;

        var headerStartPosition = m_tarPosition;

        var json = JsonSerializer.Serialize(m_writeEntries);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Calculate total size: JSON + trailer (14 bytes), then pad to 512 bytes
        const int trailerSize = 14; // 8 offset + 6 magic
        var contentSize = jsonBytes.Length + trailerSize;
        var paddedSize = (contentSize + 511) / 512 * 512;
        var paddingSize = paddedSize - contentSize;

        // Write ustar header for .eof-header (512 bytes)
        WriteUstarHeader(m_compressorStream, TarBaseConstants.EofHeaderFileName, paddedSize, DateTime.UtcNow);
        m_bytesWritten += 512;

        // Write content: JSON + padding + trailer
        m_compressorStream.Write(jsonBytes, 0, jsonBytes.Length);
        m_bytesWritten += jsonBytes.Length;
        m_compressorStream.Write(new byte[paddingSize], 0, paddingSize);
        m_bytesWritten += paddingSize;

        // Write trailer: offset (8 bytes, little-endian) + magic (6 bytes)
        var offsetBytes = BitConverter.GetBytes(headerStartPosition);
        m_compressorStream.Write(offsetBytes, 0, 8);
        m_bytesWritten += 8;
        var magicBytes = Encoding.ASCII.GetBytes(TarBaseConstants.EofHeaderMagic);
        m_compressorStream.Write(magicBytes, 0, 6);
        m_bytesWritten += 6;
    }

    private static void WriteUstarHeader(Stream stream, string fileName, long size, DateTime mtime)
    {
        var header = new byte[512];

        // Name (100 bytes) - null terminated
        var nameBytes = Encoding.UTF8.GetBytes(fileName);
        var nameLen = Math.Min(nameBytes.Length, 99);
        Array.Copy(nameBytes, 0, header, 0, nameLen);
        header[nameLen] = 0;

        // Mode (8 bytes, octal) - 0644
        Array.Copy(UstarModeBytes, 0, header, 100, 8);

        // UID (8 bytes, octal) - 0
        Array.Copy(UstarUidBytes, 0, header, 108, 8);

        // GID (8 bytes, octal) - 0
        Array.Copy(UstarGidBytes, 0, header, 116, 8);

        // Size (12 bytes, octal) - space terminated
        var sizeStr = Convert.ToString(size, 8).PadLeft(11, '0');
        var sizeBytes = Encoding.ASCII.GetBytes(sizeStr + " ");
        Array.Copy(sizeBytes, 0, header, 124, 12);

        // Mtime (12 bytes, octal) - space terminated
        var mtimeValue = new DateTimeOffset(mtime).ToUnixTimeSeconds();
        var mtimeStr = Convert.ToString(mtimeValue, 8).PadLeft(11, '0');
        var mtimeBytes = Encoding.ASCII.GetBytes(mtimeStr + " ");
        Array.Copy(mtimeBytes, 0, header, 136, 12);

        // Checksum placeholder (8 bytes) - filled with spaces for calculation
        Array.Fill(header, (byte)' ', 148, 8);

        // Type flag (1 byte) - '0' for regular file
        header[156] = (byte)'0';

        // Magic (6 bytes) - "ustar\0"
        Array.Copy(UstarMagicBytes, 0, header, 257, 6);

        // Version (2 bytes) - "00"
        Array.Copy(UstarVersionBytes, 0, header, 263, 2);

        // Calculate checksum
        var checksum = header.Sum(b => (int)b);
        var checksumStr = Convert.ToString(checksum, 8).PadLeft(6, '0');
        var checksumBytes = Encoding.ASCII.GetBytes(checksumStr + "\0 ");
        Array.Copy(checksumBytes, 0, header, 148, 8);

        stream.Write(header, 0, header.Length);
    }

    private static bool TryReadEofHeader(Stream stream, out Dictionary<string, FileEntry> entries)
    {
        entries = new Dictionary<string, FileEntry>();

        try
        {
            if (stream.Length < TarBaseConstants.EofHeaderTrailerSize)
                return false;

            stream.Seek(-TarBaseConstants.EofHeaderTrailerSize, SeekOrigin.End);

            var offsetBytes = new byte[TarBaseConstants.EofHeaderOffsetSize];
            stream.ReadExactly(offsetBytes, 0, offsetBytes.Length);
            var headerOffset = BitConverter.ToInt64(offsetBytes, 0);

            var magicBytes = new byte[TarBaseConstants.EofHeaderMagicSize];
            stream.ReadExactly(magicBytes, 0, magicBytes.Length);
            var magic = Encoding.ASCII.GetString(magicBytes);

            if (magic != TarBaseConstants.EofHeaderMagic)
                return false;

            if (headerOffset < 0 || headerOffset >= stream.Length - TarBaseConstants.EofHeaderTrailerSize)
                return false;

            stream.Seek(headerOffset, SeekOrigin.Begin);

            using var tarReader = new System.Formats.Tar.TarReader(stream, leaveOpen: true);
            var entry = tarReader.GetNextEntry();

            if (entry == null || entry.Name != TarBaseConstants.EofHeaderFileName)
                return false;

            if (entry.DataStream == null)
                return false;

            using var ms = new MemoryStream();
            entry.DataStream.CopyTo(ms);
            var contentBytes = ms.ToArray();

            if (contentBytes.Length < TarBaseConstants.EofHeaderTrailerSize)
                return false;

            var jsonBytes = contentBytes[..^TarBaseConstants.EofHeaderTrailerSize];
            int jsonLength = jsonBytes.Length;
            while (jsonLength > 0 && jsonBytes[jsonLength - 1] == 0)
                jsonLength--;

            var json = Encoding.UTF8.GetString(jsonBytes, 0, jsonLength);

            var deserialized = JsonSerializer.Deserialize<Dictionary<string, SerializableEntry>>(json);
            if (deserialized == null)
                return false;

            entries = deserialized.ToDictionary(
                kvp => kvp.Key,
                kvp => new FileEntry(kvp.Key, kvp.Value.Offset, kvp.Value.Size, DateTime.UnixEpoch.AddSeconds(kvp.Value.LastWriteTime))
            );
            return true;
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "EofHeaderReadError", ex, "Failed to read EOF header");
            entries = new Dictionary<string, FileEntry>();
            return false;
        }
    }

    private static Dictionary<string, FileEntry> BuildDictionaryByScanning(Stream stream)
    {
        var entries = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
        stream.Seek(0, SeekOrigin.Begin);

        try
        {
            using var tarReader = new System.Formats.Tar.TarReader(stream, leaveOpen: true);

            while (true)
            {
                var entryStartPosition = stream.Position;
                var entry = tarReader.GetNextEntry();
                if (entry == null)
                    break;

                if (entry.Name == TarBaseConstants.EofHeaderFileName)
                    continue;

                if (entry.EntryType != System.Formats.Tar.TarEntryType.RegularFile &&
                    entry.EntryType != System.Formats.Tar.TarEntryType.V7RegularFile)
                    continue;

                // For ustar format, header is 512 bytes, so content starts at entryStartPosition + 512
                // For pax format, headers can be variable size, but we primarily use ustar
                // Since TarReader advances the stream to after the content, we calculate backwards:
                // stream.Position is now at end of entry (after content padding)
                // Content size with padding = ((entry.Length + 511) / 512) * 512
                var paddedContentSize = (entry.Length + 511) / 512 * 512;
                var contentOffset = stream.Position - paddedContentSize;

                entries[entry.Name] = new FileEntry(
                    entry.Name,
                    contentOffset,
                    entry.Length,
                    entry.ModificationTime.UtcDateTime
                );
            }
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "TarScanError", ex, "Error scanning tar archive");
        }

        Log.WriteInformationMessage(LOGTAG, "DictionaryBuiltByScanning", "Built file dictionary by scanning {0} entries", entries.Count);
        return entries;
    }

    private record SerializableEntry(long Offset, long Size, long LastWriteTime);

    private class PendingEntryStream : WrappingAsyncStream
    {
        private readonly FileArchiveTarBased m_parent;
        private readonly string m_name;
        private readonly DateTime m_lastWriteTime;
        private bool m_closed;

        public PendingEntryStream(FileArchiveTarBased parent, string name, DateTime lastWriteTime, bool useMemoryBuffer)
            : base(useMemoryBuffer ? new MemoryStream() : Utility.TempFileStream.Create())
        {
            m_parent = parent;
            m_name = name;
            m_lastWriteTime = lastWriteTime;
        }

        public bool IsClosed => m_closed;

        protected override Task<int> ReadImplAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        protected override Task WriteImplAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (m_closed)
                throw new InvalidOperationException("Stream is closed");
            return base.BaseStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (!m_closed)
            {
                m_closed = true;
                var size = base.BaseStream.Length;
                base.BaseStream.Position = 0;
                m_parent.WriteEntryToTar(m_name, m_lastWriteTime, base.BaseStream, size);
            }
            base.Dispose(disposing);
            base.BaseStream.Dispose();
        }
    }
}
