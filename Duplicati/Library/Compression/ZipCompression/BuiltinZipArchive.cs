// Copyright (C) 2025, The Duplicati Team
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
using System.IO.Compression;
using System.Linq;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Compression.ZipCompression;

/// <summary>
/// A wrapper around the built-in .NET ZipArchive class
/// </summary>
public class BuiltinZipArchive : IZipArchive
{
    /// <summary>
    /// The log tag
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<BuiltinZipArchive>();
    /// <summary>
    /// The wrapped zip archive
    /// </summary>
    private readonly ZipArchive m_archive;
    /// <summary>
    /// The underlying stream
    /// </summary>
    private readonly Stream m_stream;
    /// <summary>
    /// Use zip64 extensions
    /// </summary>
    private readonly bool m_useZip64;

    /// <summary>
    /// Gets the number of bytes expected to be written after the stream is disposed
    /// </summary>
    private long m_flushBufferSize = 0;

    /// <summary>
    /// The compression level to use
    /// </summary>
    private readonly CompressionLevel m_compressionLevel;

    /// <summary>
    /// Lookup table for entries while in read mode
    /// </summary>
    private readonly Dictionary<string, ZipArchiveEntry> m_entries = new(Utility.Utility.ClientFilenameStringComparer);
    /// <summary>
    /// Lookup table for paths while in read mode
    /// </summary>
    private readonly Dictionary<string, string> m_pathMappings = new(Utility.Utility.ClientFilenameStringComparer);

    /// <summary>
    /// Constructs a new zip instance.
    /// Access mode is specified by mode parameter.
    /// Note that stream would not be disposed by FileArchiveZip instance so
    /// you may reuse it and have to dispose it yourself.
    /// </summary>
    /// <param name="stream">The stream to read or write depending access mode</param>
    /// <param name="mode">The archive access mode</param>
    /// <param name="options">The options passed on the commandline</param>
    public BuiltinZipArchive(Stream stream, ArchiveMode mode, ParsedZipOptions options)
    {
        m_stream = stream;
        m_useZip64 = false; // options.UseZip64;
        m_archive = new ZipArchive(
            stream,
            mode == ArchiveMode.Read ? ZipArchiveMode.Read : ZipArchiveMode.Create,
            leaveOpen: true // Mimic SharpCompress behavior, don't dispose base stream
        );

        // Built-in ZipArchive has fewer compression levels
        m_compressionLevel = options.DeflateCompressionLevel switch
        {
            SharpCompress.Compressors.Deflate.CompressionLevel.None
            or SharpCompress.Compressors.Deflate.CompressionLevel.BestSpeed
            or SharpCompress.Compressors.Deflate.CompressionLevel.Level0 => CompressionLevel.NoCompression,

            SharpCompress.Compressors.Deflate.CompressionLevel.Level1
            or SharpCompress.Compressors.Deflate.CompressionLevel.Level2
            or SharpCompress.Compressors.Deflate.CompressionLevel.Level3 => CompressionLevel.Fastest,

            SharpCompress.Compressors.Deflate.CompressionLevel.Level8
            or SharpCompress.Compressors.Deflate.CompressionLevel.Level9
            or SharpCompress.Compressors.Deflate.CompressionLevel.BestCompression => CompressionLevel.SmallestSize,

            _ => CompressionLevel.Optimal
        };

        if (mode == ArchiveMode.Read)
        {
            foreach (var entry in m_archive.Entries)
            {
                if (m_entries.ContainsKey(entry.FullName))
                    Logging.Log.WriteMessage(
                        // Warning in unittest mode to trip tests, verbose otherwise
                        options.UnittestMode ? Logging.LogMessageType.Warning : Logging.LogMessageType.Verbose,
                        LOGTAG,
                        "DuplicateArchiveEntry",
                        null,
                        $"Found duplicate entry in archive: {entry.FullName}");

                m_entries[entry.FullName] = entry;
                m_pathMappings[entry.FullName.Replace('\\', '/')] = entry.FullName;
            }
        }
        else
        {
            m_flushBufferSize = Constants.END_OF_CENTRAL_DIRECTORY_SIZE;
        }
    }

    public long FlushBufferSize => m_flushBufferSize;

    public long Size => m_archive.Mode == ZipArchiveMode.Read
        ? m_stream.Length
        : m_stream.Length + m_flushBufferSize;

    public Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
    {
        m_flushBufferSize = Constants.CENTRAL_HEADER_ENTRY_SIZE;
        if (m_useZip64)
            m_flushBufferSize += Constants.CENTRAL_HEADER_ENTRY_SIZE_ZIP64_EXTRA;
        return m_archive.CreateEntry(file, hint == CompressionHint.Noncompressible ? CompressionLevel.NoCompression : m_compressionLevel).Open();
    }

    public void Dispose()
    {
        m_archive.Dispose();
    }

    private ZipArchiveEntry? GetEntry(string file)
    {
        file = m_pathMappings.GetValueOrDefault(file, file);
        return m_entries.GetValueOrDefault(file);
    }

    private bool MatchesPrefix(string file, string prefix)
        => string.IsNullOrWhiteSpace(prefix)
            || file.StartsWith(prefix, Utility.Utility.ClientFilenameStringComparison)
            || file.Replace('\\', '/').StartsWith(prefix, Utility.Utility.ClientFilenameStringComparison);

    public bool FileExists(string file)
        => GetEntry(file) != null;

    public DateTime GetLastWriteTime(string file)
        => GetEntry(file)?.LastWriteTime.DateTime ?? new DateTime(0, DateTimeKind.Utc);

    public string[] ListFiles(string prefix)
        => m_entries.Keys.Where(x => MatchesPrefix(x, prefix))
            .ToArray();

    public IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string prefix)
        => m_entries.Select(e => new KeyValuePair<string, long>(e.Key, e.Value.Length))
            .Where(x => MatchesPrefix(x.Key, prefix));

    public Stream? OpenRead(string file)
        => m_archive.GetEntry(file)?.Open();
}
