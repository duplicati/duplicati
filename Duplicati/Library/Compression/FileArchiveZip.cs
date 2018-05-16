#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.IO;
using Duplicati.Library.Interface;
using SharpCompress.Common;

using SharpCompress.Archives;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using SharpCompress.Readers;
using System.Linq;

namespace Duplicati.Library.Compression
{
    /// <summary>
    /// An abstraction of a zip archive as a FileArchive, based on SharpCompress.
    /// Please note, duplicati does not require both Read &amp; Write access at the same time so this has not been implemented.
    /// </summary>
    public class FileArchiveZip : ICompression
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<FileArchiveZip>();

        private const string CannotReadWhileWriting = "Cannot read while writing";
        private const string CannotWriteWhileReading = "Cannot write while reading";

        /// <summary>
        /// The commandline option for toggling the compression level
        /// </summary>
        private const string COMPRESSION_LEVEL_OPTION = "zip-compression-level";

        /// <summary>
        /// The old commandline option for toggling the compression level
        /// </summary>
        private const string COMPRESSION_LEVEL_OPTION_ALIAS = "compression-level";

        /// <summary>
        /// The commandline option for toggling the compression method
        /// </summary>
        private const string COMPRESSION_METHOD_OPTION = "zip-compression-method";
        /// <summary>
        /// The commandline option for toggling the zip64 support
        /// </summary>
        private const string COMPRESSION_ZIP64_OPTION = "zip-compression-zip64";

        /// <summary>
        /// The default compression level
        /// </summary>
        private const SharpCompress.Compressors.Deflate.CompressionLevel DEFAULT_COMPRESSION_LEVEL = SharpCompress.Compressors.Deflate.CompressionLevel.Level9;

        /// <summary>
        /// The default compression method
        /// </summary>
        private const CompressionType DEFAULT_COMPRESSION_METHOD = CompressionType.Deflate;

        /// <summary>
        /// The default setting for the zip64 support
        /// </summary>
        private const bool DEFAULT_ZIP64 = false;

        /// <summary>
        /// Taken from SharpCompress ZipCentralDirectorEntry.cs
        /// </summary>
        private const int CENTRAL_HEADER_ENTRY_SIZE = 8 + 2 + 2 + 4 + 4 + 4 + 4 + 2 + 2 + 2 + 2 + 2 + 2 + 2 + 4;

        /// <summary>
        /// The size of the extended zip64 header
        /// </summary>
        private const int CENTRAL_HEADER_ENTRY_SIZE_ZIP64_EXTRA = 2 + 2 + 8 + 8 + 8 + 4;

        /// <summary>
        /// This property indicates reading or writing access mode of the file archive.
        /// </summary>
        ArchiveMode m_mode;

        /// <summary>
        /// Gets the number of bytes expected to be written after the stream is disposed
        /// </summary>
        private long m_flushBufferSize = 0;

        /// <summary>
        /// The ZipArchive instance used when reading archives
        /// </summary>
        private IArchive m_archive;
        /// <summary>
        /// The stream used to either read or write
        /// </summary>
        private Stream m_stream;

        /// <summary>
        /// Lookup table for faster access to entries based on their name.
        /// </summary>
        private Dictionary<string, IEntry> m_entryDict;

        /// <summary>
        /// The writer instance used when creating archives
        /// </summary>
        private IWriter m_writer;

        /// <summary>
        /// A flag indicating if we are using the fail-over reader interface
        /// </summary>
        public bool m_using_reader = false;

        /// <summary>
        /// The compression level applied when the hint does not indicate incompressible
        /// </summary>
        private SharpCompress.Compressors.Deflate.CompressionLevel m_defaultCompressionLevel;

        /// <summary>
        /// The compression level applied when the hint does not indicate incompressible
        /// </summary>
        private CompressionType m_compressionType;

        /// <summary>
        /// A flag indicating if zip64 is in use
        /// </summary>
        private bool m_usingZip64;

        /// <summary>
        /// Default constructor, used to read file extension and supported commands
        /// </summary>
        public FileArchiveZip() { }

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
            SharpCompress.Readers.Zip.ZipReader rd = null;

            try
            {
                rd = SharpCompress.Readers.Zip.ZipReader.Open(m_stream);

                while (rd.MoveToNextEntry())
                    if (entry.Key == rd.Entry.Key)
                        return new StreamWrapper(rd.OpenEntryStream(), stream => {
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
        /// Constructs a new zip instance.
        /// Access mode is specified by mode parameter.
        /// Note that stream would not be disposed by FileArchiveZip instance so
        /// you may reuse it and have to dispose it yourself.
        /// </summary>
        /// <param name="stream">The stream to read or write depending access mode</param>
        /// <param name="mode">The archive acces mode</param>
        /// <param name="options">The options passed on the commandline</param>
        public FileArchiveZip(Stream stream, ArchiveMode mode, IDictionary<string, string> options)
        {
            m_stream = stream;
            m_mode = mode;
            if (mode == ArchiveMode.Write)
            {
                var compression = new ZipWriterOptions(CompressionType.Deflate);

                compression.CompressionType = DEFAULT_COMPRESSION_METHOD;
                compression.DeflateCompressionLevel = DEFAULT_COMPRESSION_LEVEL;

                m_usingZip64 = compression.UseZip64 =
                    options.ContainsKey(COMPRESSION_ZIP64_OPTION)
                    ? Duplicati.Library.Utility.Utility.ParseBoolOption(options, COMPRESSION_ZIP64_OPTION)
                    : DEFAULT_ZIP64;

                string cpmethod;
                CompressionType tmptype;
                if (options.TryGetValue(COMPRESSION_METHOD_OPTION, out cpmethod) && Enum.TryParse<SharpCompress.Common.CompressionType>(cpmethod, true, out tmptype))
                    compression.CompressionType = tmptype;

                string cplvl;
                int tmplvl;
                if (options.TryGetValue(COMPRESSION_LEVEL_OPTION, out cplvl) && int.TryParse(cplvl, out tmplvl))
                    compression.DeflateCompressionLevel = (SharpCompress.Compressors.Deflate.CompressionLevel)Math.Max(Math.Min(9, tmplvl), 0);
                else if (options.TryGetValue(COMPRESSION_LEVEL_OPTION_ALIAS, out cplvl) && int.TryParse(cplvl, out tmplvl))
                    compression.DeflateCompressionLevel = (SharpCompress.Compressors.Deflate.CompressionLevel)Math.Max(Math.Min(9, tmplvl), 0);

                m_defaultCompressionLevel = compression.DeflateCompressionLevel;
                m_compressionType = compression.CompressionType;

                m_writer = WriterFactory.Open(m_stream, ArchiveType.Zip, compression);

                //Size of endheader, taken from SharpCompress ZipWriter
                m_flushBufferSize = 8 + 2 + 2 + 4 + 4 + 2 + 0;
            }
        }

        #region IFileArchive Members
        /// <summary>
        /// Gets the filename extension used by the compression module
        /// </summary>
        public string FilenameExtension { get { return "zip"; } }
        /// <summary>
        /// Gets a friendly name for the compression module
        /// </summary>
        public string DisplayName { get { return Strings.FileArchiveZip.DisplayName; } }
        /// <summary>
        /// Gets a description of the compression module
        /// </summary>
        public string Description { get { return Strings.FileArchiveZip.Description; } }

        /// <summary>
        /// Gets a list of commands supported by the compression module
        /// </summary>
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                // This is the cross between these two:
                // https://github.com/adamhathcock/sharpcompress/blob/master/src/SharpCompress/Common/Zip/ZipCompressionMethod.cs
                // https://github.com/adamhathcock/sharpcompress/blob/master/src/SharpCompress/Common/CompressionType.cs

                var methods = new[]
                {
                    CompressionType.None.ToString(),
                    CompressionType.Deflate.ToString(),
                    CompressionType.BZip2.ToString(),
                    CompressionType.LZMA.ToString(),
                    CompressionType.PPMd.ToString(),
                };

                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(COMPRESSION_LEVEL_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionlevelShort, Strings.FileArchiveZip.CompressionlevelLong, DEFAULT_COMPRESSION_LEVEL.ToString(), null, new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"}),
                    new CommandLineArgument(COMPRESSION_LEVEL_OPTION_ALIAS, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionlevelShort, Strings.FileArchiveZip.CompressionlevelLong, DEFAULT_COMPRESSION_LEVEL.ToString(), null, new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"}, Strings.FileArchiveZip.CompressionlevelDeprecated(COMPRESSION_LEVEL_OPTION)),
                    new CommandLineArgument(COMPRESSION_METHOD_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionmethodShort, Strings.FileArchiveZip.CompressionmethodLong(COMPRESSION_LEVEL_OPTION), DEFAULT_COMPRESSION_METHOD.ToString(), null, methods),
                    new CommandLineArgument(COMPRESSION_ZIP64_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.FileArchiveZip.Compressionzip64Short, Strings.FileArchiveZip.Compressionzip64Long, DEFAULT_ZIP64.ToString())
                });
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
            LoadEntryTable();
            var q = m_entryDict.Values.AsEnumerable();
            if (!string.IsNullOrEmpty(prefix))
                q = q.Where(x =>
                            x.Key.StartsWith(prefix, Duplicati.Library.Utility.Utility.ClientFilenameStringComparison)
                            ||
                            x.Key.Replace('\\', '/').StartsWith(prefix, Duplicati.Library.Utility.Utility.ClientFilenameStringComparison)
                           );

            return q.Select(x => new KeyValuePair<string, long>(x.Key, x.Size)).ToArray();
        }

        /// <summary>
        /// Opens an file for reading
        /// </summary>
        /// <param name="file">The name of the file to open</param>
        /// <returns>A stream with the file contents</returns>
        public Stream OpenRead(string file)
        {
            if (m_mode != ArchiveMode.Read)
                throw new InvalidOperationException(CannotReadWhileWriting);

            var ze = GetEntry(file);
            if (ze == null)
                return null;

            if (ze is IArchiveEntry)
                return ((IArchiveEntry)ze).OpenEntryStream();
            else if (ze is SharpCompress.Common.Zip.ZipEntry)
                return GetStreamFromReader(ze);

            throw new Exception(string.Format("Unexpected result: {0}", ze.GetType().FullName));

        }

        /// <summary>
        /// Helper method to load the entry table
        /// </summary>
        private void LoadEntryTable()
        {
            if (m_entryDict == null)
            {
                try
                {
                    var d = new Dictionary<string, IEntry>(Duplicati.Library.Utility.Utility.ClientFilenameStringComparer);
                    foreach (var en in Archive.Entries)
                        d[en.Key] = en;
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

                        Logging.Log.WriteWarningMessage(LOGTAG, "BrokenCentralHeader", ex2, "Zip archive appears to have broken records, returning the {0} records that could be recovered", d.Count);
                    }

                    m_entryDict = d;
                }
            }
        }

        /// <summary>
        /// Internal function that returns a ZipEntry for a filename, or null if no such file exists
        /// </summary>
        /// <param name="file">The name of the file to find</param>
        /// <returns>The ZipEntry for the file or null if no such file was found</returns>
        private IEntry GetEntry(string file)
        {
            if (m_mode != ArchiveMode.Read)
                throw new InvalidOperationException(CannotReadWhileWriting);

            LoadEntryTable();

            IEntry e;
            if (m_entryDict.TryGetValue(file, out e))
                return e;
            if (m_entryDict.TryGetValue(file.Replace('/', '\\'), out e))
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
                throw new InvalidOperationException(CannotWriteWhileReading);

            m_flushBufferSize += CENTRAL_HEADER_ENTRY_SIZE + System.Text.Encoding.UTF8.GetByteCount(file);
            if (m_usingZip64)
                m_flushBufferSize += CENTRAL_HEADER_ENTRY_SIZE_ZIP64_EXTRA;

            return ((ZipWriter)m_writer).WriteToStream(file, new ZipWriterEntryOptions()
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
                throw new InvalidOperationException(CannotReadWhileWriting);

            return GetEntry(file) != null;
        }

        /// <summary>
        /// Gets the current size of the archive
        /// </summary>
        public long Size
        {
            get
            {
                return m_mode == ArchiveMode.Write ? m_stream.Length : Archive.TotalSize;
            }
        }

        /// <summary>
        /// The size of the current unflushed buffer
        /// </summary>
        public long FlushBufferSize
        {
            get
            {
                return m_flushBufferSize;
            }
        }


        /// <summary>
        /// Gets the last write time for a file
        /// </summary>
        /// <param name="file">The name of the file to query</param>
        /// <returns>The last write time for the file</returns>
        public DateTime GetLastWriteTime(string file)
        {
            IEntry entry = GetEntry(file);
            if (entry != null)
            {
                if (entry.LastModifiedTime.HasValue)
                    return entry.LastModifiedTime.Value;
                else
                    return DateTime.MinValue;
            }

            throw new FileNotFoundException(Strings.FileArchiveZip.FileNotFoundError(file));
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_archive != null)
                m_archive.Dispose();
            m_archive = null;

            if (m_writer != null)
                m_writer.Dispose();
            m_writer = null;

            m_stream = null;
        }

        #endregion

    }
}
