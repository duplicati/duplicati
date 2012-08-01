#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
using System.Linq;
using SharpCompress.Common;

using SharpCompress.Archive;
using SharpCompress.Archive.Zip;
using SharpCompress.Writer;
using SharpCompress.Writer.Zip;

namespace Duplicati.Library.Compression
{
    /// <summary>
    /// An abstraction of a zip archive as a FileArchive, based on SharpCompress.
    /// Please note, duplicati does not require both Read & Write access at the same time so this has not been implemented
    /// </summary>
    public class FileArchiveZip : ICompression
    {
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
        /// The default compression level
        /// </summary>
        private const SharpCompress.Compressor.Deflate.CompressionLevel DEFAULT_COMPRESSION_LEVEL = SharpCompress.Compressor.Deflate.CompressionLevel.Level9;

        /// <summary>
        /// The default compression method
        /// </summary>
        private const CompressionType DEFAULT_COMPRESSION_METHOD = CompressionType.Deflate;

        /// <summary>
        /// Taken from SharpCompress ZipCentralDirectorEntry.cs
        /// </summary>
        private const int CENTRAL_HEADER_ENTRY_SIZE = 8 + 2 + 2 + 4 + 4 + 4 + 4 + 2 + 2 + 2 + 2 + 2 + 2 + 2 + 4;

        /// <summary>
        /// This property indicates that this current instance should write to a file
        /// </summary>
        private bool m_isWriting;

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
        /// The writer instance used when creating archives
        /// </summary>
        private IWriter m_writer;

        /// <summary>
        /// Default constructor, used to read file extension and supported commands
        /// </summary>
        public FileArchiveZip() { }

        /// <summary>
        /// Constructs a new zip instance.
        /// If the file exists and has a non-zero length we read it,
        /// otherwise we create a new archive.
        /// </summary>
        /// <param name="filename">The name of the file to read or write</param>
        /// <param name="options">The options passed on the commandline</param>
        public FileArchiveZip(string filename, Dictionary<string, string> options)
        {
            if (string.IsNullOrEmpty(filename) && filename.Trim().Length == 0)
                throw new ArgumentException("filename");

            if (!File.Exists(filename) || new FileInfo(filename).Length > 0)
            {
                m_isWriting = false;
                m_stream = new System.IO.FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
                m_archive = ArchiveFactory.Open(m_stream);
            }
            else
            {
                CompressionInfo cfi = new CompressionInfo();
                cfi.Type = DEFAULT_COMPRESSION_METHOD;
                cfi.DeflateCompressionLevel = DEFAULT_COMPRESSION_LEVEL;

                string cpmethod;
                CompressionType tmptype;
                if (options.TryGetValue(COMPRESSION_METHOD_OPTION, out cpmethod) && Enum.TryParse<SharpCompress.Common.CompressionType>(cpmethod, true, out tmptype))
                    cfi.Type = tmptype;

                string cplvl;
                int tmplvl;
                if (options.TryGetValue(COMPRESSION_LEVEL_OPTION, out cplvl) && int.TryParse(cplvl, out tmplvl))
                    cfi.DeflateCompressionLevel = (SharpCompress.Compressor.Deflate.CompressionLevel)Math.Max(Math.Min(9, tmplvl), 0);
                else if (options.TryGetValue(COMPRESSION_LEVEL_OPTION_ALIAS, out cplvl) && int.TryParse(cplvl, out tmplvl))
                    cfi.DeflateCompressionLevel = (SharpCompress.Compressor.Deflate.CompressionLevel)Math.Max(Math.Min(9, tmplvl), 0);

                m_isWriting = true;
                m_stream = new System.IO.FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Read);
                m_writer = WriterFactory.Open(m_stream, ArchiveType.Zip, cfi);

                //Size of endheader, taken from SharpCompress ZipWriter
                m_flushBufferSize = 8 + 2 + 2 + 4 + 4 + 2 + 0;
            }
        }

        /// <summary>
        /// Converts a zip file path to the format used by the local filesystem.
        /// Internally all files are stored in the archive use / as directory separator.
        /// </summary>
        /// <param name="path">The path to convert in internal format</param>
        /// <returns>The path in filesystem format</returns>
        private static string PathToOsFilesystem(string path)
        {
            return Path.DirectorySeparatorChar != '/' ? path.Replace('/', Path.DirectorySeparatorChar) : path;
        }

        /// <summary>
        /// Converts a file system path to the internal format.
        /// Internally all files are stored in the archive use / as directory separator.
        /// </summary>
        /// <param name="path">The path to convert in filesystem format</param>
        /// <returns>The path in the internal format</returns>
        private static string PathToZipFilesystem(string path)
        {
            return Path.DirectorySeparatorChar != '/' ? path.Replace(Path.DirectorySeparatorChar, '/') : path;
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
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(COMPRESSION_LEVEL_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionlevelShort, Strings.FileArchiveZip.CompressionlevelLong, DEFAULT_COMPRESSION_LEVEL.ToString(), null, new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"}),
                    new CommandLineArgument(COMPRESSION_LEVEL_OPTION_ALIAS, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionlevelShort, Strings.FileArchiveZip.CompressionlevelLong, DEFAULT_COMPRESSION_LEVEL.ToString(), null, new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"}, string.Format(Strings.FileArchiveZip.CompressionlevelDeprecated, COMPRESSION_LEVEL_OPTION)),
                    new CommandLineArgument(COMPRESSION_METHOD_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionmethodShort, string.Format(Strings.FileArchiveZip.CompressionmethodLong, COMPRESSION_LEVEL_OPTION), DEFAULT_COMPRESSION_METHOD.ToString(), null, Enum.GetNames(typeof(CompressionType)))
                });
            }
        }

        /// <summary>
        /// Filters entries based on a prefix
        /// </summary>
        /// <param name="prefix">An optional prefix</param>
        /// <returns>A list of filenames</returns>
        private IEnumerable<string> FilterEntries(string prefix)
        {
            prefix = PathToZipFilesystem(prefix);
            List<string> results = new List<string>();
            foreach(IArchiveEntry e in m_archive.Entries)
            {
                if (prefix == null || e.FilePath.StartsWith(prefix, Duplicati.Library.Utility.Utility.ClientFilenameStringComparision))
                    results.Add(PathToOsFilesystem(e.FilePath));
            }

            return results;
        }

        /// <summary>
        /// Returns a list of files matching the given prefix
        /// </summary>
        /// <param name="prefix">The prefix to match</param>
        /// <returns>A list of files matching the prefix</returns>
        public string[] ListFiles(string prefix)
        {
            return FilterEntries(prefix).ToArray();
        }

        /// <summary>
        /// Returns a list of entries matching the given prefix
        /// </summary>
        /// <param name="prefix">The prefix to match</param>
        /// <returns>A list of entries matching the prefix</returns>
        public string[] ListEntries(string prefix)
        {
            return FilterEntries(prefix).ToArray();
        }

        /// <summary>
        /// Reads all bytes from a file and returns it as an array
        /// </summary>
        /// <param name="file">The name of the file to read</param>
        /// <returns>The contents of the file as a byte array</returns>
        public byte[] ReadAllBytes(string file)
        {
            using (var ms = new MemoryStream())
            using (var s = OpenRead(file))
            {
                Utility.Utility.CopyStream(s, ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Reads all lines from a text file, and returns them
        /// </summary>
        /// <param name="file">The name of the file to read</param>
        /// <returns>The lines read from the file</returns>
        public string[] ReadAllLines(string file)
        {
            var lines = new List<string>();
            using (var sr = new StreamReader(OpenRead(file), System.Text.Encoding.UTF8, true))
                while (!sr.EndOfStream)
                    lines.Add(sr.ReadLine());

            return lines.ToArray();
        }

        /// <summary>
        /// Opens an file for reading
        /// </summary>
        /// <param name="file">The name of the file to open</param>
        /// <returns>A stream with the file contents</returns>
        public Stream OpenRead(string file)
        {
            if (m_isWriting)
                throw new InvalidOperationException("Cannot read while writing");

            IArchiveEntry ze = GetEntry(file);

            return ze == null ? null : ze.OpenEntryStream();
        }

        /// <summary>
        /// Opens a file for writing
        /// </summary>
        /// <param name="file">The name of the file to write</param>
        /// <returns>A stream that can be updated with file contents</returns>
        public Stream OpenWrite(string file)
        {
            return CreateFile(file);
        }

        /// <summary>
        /// Writes all bytes from the array into the file
        /// </summary>
        /// <param name="file">The name of the file</param>
        /// <param name="data">The data contents of the file</param>
        public void WriteAllBytes(string file, byte[] data)
        {
            using (var s = CreateFile(file))
                s.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Writes the given lines into the file
        /// </summary>
        /// <param name="file">The name of the file</param>
        /// <param name="data">The lines to write</param>
        public void WriteAllLines(string file, string[] data)
        {
            using (var sw = new StreamWriter(CreateFile(file), System.Text.Encoding.UTF8))
                foreach (var s in data)
                    sw.WriteLine(s);
        }

        /// <summary>
        /// Internal function that returns a ZipEntry for a filename, or null if no such file exists
        /// </summary>
        /// <param name="file">The name of the file to find</param>
        /// <returns>The ZipEntry for the file or null if no such file was found</returns>
        private IArchiveEntry GetEntry(string file)
        {
            if (m_isWriting)
                throw new InvalidOperationException("Cannot read while writing");

            string path = PathToZipFilesystem(file);
            foreach(IArchiveEntry e in m_archive.Entries)
            {
                if (string.Equals(e.FilePath, path, Duplicati.Library.Utility.Utility.ClientFilenameStringComparision))
                    return e;
            }

            string path2 = PathToOsFilesystem(file);
            if (path != path2)
            {
                path = path2;
                foreach (IArchiveEntry e in m_archive.Entries)
                {
                    if (string.Equals(e.FilePath, path, Duplicati.Library.Utility.Utility.ClientFilenameStringComparision))
                        return e;
                }
            }

            return null;
        }

        /// <summary>
        /// Deletes a file from the archive
        /// </summary>
        /// <param name="file">The name of the file to delete</param>
        public virtual void DeleteFile(string file)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a file in the archive and returns a writeable stream
        /// </summary>
        /// <param name="file">The name of the file to create</param>
        /// <returns>A writeable stream for the file contents</returns>
        public Stream CreateFile(string file)
        {
            return CreateFile(file, DateTime.Now);
        }

        /// <summary>
        /// Creates a file in the archive and returns a writeable stream
        /// </summary>
        /// <param name="file">The name of the file to create</param>
        /// <param name="lastWrite">The time the file was last written</param>
        /// <returns>A writeable stream for the file contents</returns>
        public virtual Stream CreateFile(string file, DateTime lastWrite)
        {
            if (!m_isWriting)
                throw new InvalidOperationException("Cannot write while reading");

            m_flushBufferSize += CENTRAL_HEADER_ENTRY_SIZE + System.Text.Encoding.UTF8.GetByteCount(file);
            return ((ZipWriter)m_writer).WriteToStream(file, lastWrite, null);

        }

        /// <summary>
        /// Returns a value that indicates if the file exists
        /// </summary>
        /// <param name="file">The name of the file to test existence for</param>
        /// <returns>True if the file exists, false otherwise</returns>
        public bool FileExists(string file)
        {
            if (m_isWriting)
                throw new InvalidOperationException("Cannot read while writing");

            return GetEntry(file) != null;
        }

        /// <summary>
        /// Gets the current size of the archive
        /// </summary>
        public long Size
        {
            get
            {
                return m_archive == null ? m_stream.Length : m_archive.TotalSize;
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

            throw new FileNotFoundException(string.Format(Strings.FileArchiveZip.FileNotFoundError, file));
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

            if (m_stream != null)
                m_stream.Dispose();
            m_stream = null;
        }

        #endregion

    }
}
