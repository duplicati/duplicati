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
using System.Linq;
using Duplicati.Library.Interface;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

namespace Duplicati.Library.Compression.ZipCompression;

public class SharpCompressZipArchive : IZipArchive
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<SharpCompressZipArchive>();

    /// <summary>
    /// This property indicates reading or writing access mode of the file archive.
    /// </summary>
    private readonly ArchiveMode m_mode;

    /// <summary>
    /// The ZipArchive instance used when reading archives
    /// </summary>
    private IArchive? m_archive;

    /// <summary>
    /// The stream used to either read or write
    /// </summary>
    private readonly Stream m_stream;

    /// <summary>
    /// Lookup table for faster access to entries based on their name.
    /// </summary>
    private Dictionary<string, IEntry>? m_entryDict;

    /// <summary>
    /// The writer instance used when creating archives
    /// </summary>
    private readonly IWriter? m_writer;

    /// <summary>
    /// A flag indicating if we are using the fail-over reader interface
    /// </summary>
    public bool m_using_reader = false;

    /// <summary>
    /// Gets the number of bytes expected to be written after the stream is disposed
    /// </summary>
    private long m_flushBufferSize = 0;

    /// <summary>
    /// Flag indicating if we are using Zip64 extensions
    /// </summary>
    private readonly bool m_usingZip64;
    /// <summary>
    /// The default compression level to use
    /// </summary>
    private readonly CompressionLevel m_defaultCompressionLevel;
    /// <summary>
    /// The compression algorithm
    /// </summary>
    private readonly CompressionType m_compressionType;
    /// <summary>
    /// Flag indicating if we are in unittest mode
    /// </summary>
    private readonly bool m_unittestMode;

    /// <summary>
    /// Constructs a new Zip instance.
    /// Access mode is specified by mode parameter.
    /// Note that stream would not be disposed by FileArchiveZip instance so
    /// you may reuse it and have to dispose it yourself.
    /// </summary>
    /// <param name="stream">The stream to read or write depending access mode</param>
    /// <param name="mode">The archive access mode</param>
    /// <param name="options">The options passed on the commandline</param>
    public SharpCompressZipArchive(Stream stream, ArchiveMode mode, ParsedZipOptions options)
    {
        m_stream = stream;
        m_mode = mode;

        m_usingZip64 = options.UseZip64;
        m_defaultCompressionLevel = options.DeflateCompressionLevel;
        m_compressionType = options.CompressionType;
        m_mode = mode;
        m_unittestMode = options.UnittestMode;

        if (mode == ArchiveMode.Write)
        {
            var compression = new ZipWriterOptions(CompressionType.Deflate)
            {
                CompressionType = m_compressionType,
                DeflateCompressionLevel = m_defaultCompressionLevel
            };

            m_writer = WriterFactory.Open(m_stream, ArchiveType.Zip, compression);
            m_flushBufferSize = Constants.END_OF_CENTRAL_DIRECTORY_SIZE;
        }
    }

    private IArchive Archive
    {
        get
        {
            if (m_archive == null)
            {
                m_stream.Position = 0;
                m_archive = ArchiveFactory.Open(m_stream);
            }
            return m_archive;
        }
    }

    public void SwitchToReader()
    {
        if (!m_using_reader)
        {
            // Close what we have
            using (m_stream)
            using (m_archive)
            { }

            m_using_reader = true;
        }
    }

    public Stream GetStreamFromReader(IEntry entry)
    {
        SharpCompress.Readers.Zip.ZipReader? rd = null;

        try
        {
            rd = SharpCompress.Readers.Zip.ZipReader.Open(m_stream);

            while (rd.MoveToNextEntry())
                if (entry.Key == rd.Entry.Key)
                    return new StreamWrapper(rd.OpenEntryStream(), stream =>
                    {
                        rd.Dispose();
                    });

            throw new Exception(string.Format("Stream not found: {0}", entry.Key));
        }
        catch
        {
            if (rd != null)
                rd.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Returns a list of files matching the given prefix
    /// </summary>
    /// <param name="prefix">The prefix to match</param>
    /// <returns>A list of files matching the prefix</returns>
    public string[] ListFiles(string prefix)
    {
        return ListFilesWithSize(prefix).Select(x => x.Key).ToArray();
    }

    /// <summary>
    /// Returns a list of files matching the given prefix
    /// </summary>
    /// <param name="prefix">The prefix to match</param>
    /// <returns>A list of files matching the prefix</returns>
    public IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string prefix)
    {
        var q = EntryDict.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(prefix))
            q = q.Where(x =>
                        x.Key.StartsWith(prefix, Utility.Utility.ClientFilenameStringComparison)
                        ||
                        x.Key.Replace('\\', '/').StartsWith(prefix, Utility.Utility.ClientFilenameStringComparison)
                       );

        return q.Select(x => new KeyValuePair<string, long>(x.Key, x.Size)).ToArray();
    }

    /// <summary>
    /// Opens an file for reading
    /// </summary>
    /// <param name="file">The name of the file to open</param>
    /// <returns>A stream with the file contents</returns>
    public Stream? OpenRead(string file)
    {
        if (m_mode != ArchiveMode.Read)
            throw new InvalidOperationException(Constants.CannotReadWhileWriting);

        var ze = GetEntry(file);
        if (ze == null)
            return null;

        if (ze is IArchiveEntry entry)
            return entry.OpenEntryStream();
        else if (ze is SharpCompress.Common.Zip.ZipEntry)
            return GetStreamFromReader(ze);

        throw new Exception(string.Format("Unexpected result: {0}", ze.GetType().FullName));

    }

    /// <summary>
    /// Initializes and returns the entry dictionary
    /// </summary>
    private Dictionary<string, IEntry> EntryDict => LoadEntryTable();

    /// <summary>
    /// Helper method to load the entry table
    /// </summary>
    private Dictionary<string, IEntry> LoadEntryTable()
    {
        if (m_entryDict == null)
        {
            try
            {
                var d = new Dictionary<string, IEntry>(Duplicati.Library.Utility.Utility.ClientFilenameStringComparer);
                foreach (var en in Archive.Entries)
                {
                    if (d.ContainsKey(en.Key))
                        Logging.Log.WriteMessage(
                            // Warning in unittest mode to trip tests, verbose otherwise
                            m_unittestMode ? Logging.LogMessageType.Warning : Logging.LogMessageType.Verbose,
                            LOGTAG,
                            "DuplicateArchiveEntry",
                            null,
                            $"Found duplicate entry in archive: {en.Key}");

                    d[en.Key] = en;
                }
                m_entryDict = d;
            }
            catch (Exception ex)
            {
                // If we get an exception here, it may be caused by the Central Header
                // being defect, so we switch to the less efficient reader interface
                if (m_using_reader)
                    throw;

                Logging.Log.WriteWarningMessage(LOGTAG, "BrokenCentralHeaderFallback", ex, "Zip archive appears to have a broken Central Record Header, switching to stream mode");
                SwitchToReader();

                var d = new Dictionary<string, IEntry>(Duplicati.Library.Utility.Utility.ClientFilenameStringComparer);

                try
                {
                    using (var rd = SharpCompress.Readers.Zip.ZipReader.Open(m_stream, new ReaderOptions() { LookForHeader = false }))
                        while (rd.MoveToNextEntry())
                        {
                            d[rd.Entry.Key] = rd.Entry;

                            // Some streams require this
                            // to correctly find the next entry
                            using (rd.OpenEntryStream())
                            { }
                        }
                }
                catch (Exception ex2)
                {
                    // If we have zero files, or just a manifest, don't bother
                    if (d.Count < 2)
                        throw;

                    Logging.Log.WriteWarningMessage(LOGTAG, "BrokenCentralHeader", ex2, "ZIP archive appears to have broken records, returning the {0} records that could be recovered", d.Count);
                }

                m_entryDict = d;
            }
        }

        return m_entryDict;
    }

    /// <summary>
    /// Internal function that returns a ZipEntry for a filename, or null if no such file exists
    /// </summary>
    /// <param name="file">The name of the file to find</param>
    /// <returns>The ZipEntry for the file or null if no such file was found</returns>
    private IEntry? GetEntry(string file)
    {
        if (m_mode != ArchiveMode.Read)
            throw new InvalidOperationException(Constants.CannotReadWhileWriting);

        var dict = LoadEntryTable();

        if (dict.TryGetValue(file, out var e))
            return e;
        if (dict.TryGetValue(file.Replace('/', '\\'), out e))
            return e;

        return null;
    }

    /// <summary>
    /// Creates a file in the archive and returns a writeable stream
    /// </summary>
    /// <param name="file">The name of the file to create</param>
    /// <param name="hint">A hint to the compressor as to how compressible the file data is</param>
    /// <param name="lastWrite">The time the file was last written</param>
    /// <returns>A writeable stream for the file contents</returns>
    public virtual Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
    {
        if (m_mode != ArchiveMode.Write)
            throw new InvalidOperationException(Constants.CannotWriteWhileReading);

        m_flushBufferSize += Constants.CENTRAL_HEADER_ENTRY_SIZE + System.Text.Encoding.UTF8.GetByteCount(file);
        if (m_usingZip64)
            m_flushBufferSize += Constants.CENTRAL_HEADER_ENTRY_SIZE_ZIP64_EXTRA;

        return ((ZipWriter)m_writer!).WriteToStream(file, new ZipWriterEntryOptions()
        {
            DeflateCompressionLevel = hint == CompressionHint.Noncompressible ? SharpCompress.Compressors.Deflate.CompressionLevel.None : m_defaultCompressionLevel,
            ModificationDateTime = lastWrite,
            CompressionType = m_compressionType
        });
    }

    /// <summary>
    /// Returns a value that indicates if the file exists
    /// </summary>
    /// <param name="file">The name of the file to test existence for</param>
    /// <returns>True if the file exists, false otherwise</returns>
    public bool FileExists(string file)
    {
        if (m_mode != ArchiveMode.Read)
            throw new InvalidOperationException(Constants.CannotReadWhileWriting);

        return GetEntry(file) != null;
    }

    /// <summary>
    /// Gets the current size of the archive
    /// </summary>
    public long Size => m_mode == ArchiveMode.Write
        ? m_stream.Length
        : Archive.TotalSize;

    /// <summary>
    /// The size of the current unflushed buffer
    /// </summary>
    public long FlushBufferSize => m_flushBufferSize;


    /// <summary>
    /// Gets the last write time for a file
    /// </summary>
    /// <param name="file">The name of the file to query</param>
    /// <returns>The last write time for the file</returns>
    public DateTime GetLastWriteTime(string file)
    {
        var entry = GetEntry(file);
        if (entry != null)
        {
            if (entry.LastModifiedTime.HasValue)
                return entry.LastModifiedTime.Value;
            else
                return DateTime.MinValue;
        }

        throw new FileNotFoundException(Strings.FileArchiveZip.FileNotFoundError(file));
    }

    public void Dispose()
    {
        if (m_archive != null)
            m_archive.Dispose();
        m_archive = null;

        if (m_writer != null)
            m_writer.Dispose();
    }

}
