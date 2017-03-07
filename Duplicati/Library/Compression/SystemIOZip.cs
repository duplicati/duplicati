//  Copyright (C) 2017, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Compression
{
    public class SystemIOZip : ICompression
    {
        private readonly bool m_isWriting;

        /// <summary>
        /// The commandline option for toggling the compression level
        /// </summary>
        private const string COMPRESSION_LEVEL_OPTION = "zip-compression-level";

        /// <summary>
        /// The old commandline option for toggling the compression level
        /// </summary>
        private const string COMPRESSION_LEVEL_OPTION_ALIAS = "compression-level";

        /// <summary>
        /// The default compression level
        /// </summary>
        private const int DEFAULT_COMPRESSION_LEVEL = 9;

        /// <summary>
        /// The selected compression level
        /// </summary>
        private readonly CompressionLevel m_compressionLevel = CompressionLevel.Optimal;

        /// <summary>
        /// Taken from SharpCompress ZipCentralDirectorEntry.cs
        /// </summary>
        private const int CENTRAL_HEADER_ENTRY_SIZE = 8 + 2 + 2 + 4 + 4 + 4 + 4 + 2 + 2 + 2 + 2 + 2 + 2 + 2 + 4;

        /// <summary>
        /// The archive we are wrapping
        /// </summary>
        private readonly ZipArchive m_archive;

        /// <summary>
        /// The stream backing the archive
        /// </summary>
        private readonly Stream m_stream;

        /// <summary>
        /// Default constructor, used to read file extension and supported commands
        /// </summary>
        public SystemIOZip() { }

        /// <summary>
        /// Constructs a new zip instance.
        /// If the file exists and has a non-zero length we read it,
        /// otherwise we create a new archive.
        /// </summary>
        /// <param name="filename">The name of the file to read or write</param>
        /// <param name="options">The options passed on the commandline</param>
        public SystemIOZip(string filename, Dictionary<string, string> options)
        {
            if (string.IsNullOrEmpty(filename) && filename.Trim().Length == 0)
                throw new ArgumentException("filename");

            if (File.Exists(filename) && new FileInfo(filename).Length > 0)
            {
                m_stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
                m_isWriting = false;
                m_archive = new ZipArchive(
                    m_stream,
                    ZipArchiveMode.Read,
                    false);
            }
            else
            {
                
                int cmplevel = DEFAULT_COMPRESSION_LEVEL;
                string cplvl;
                int tmplvl;
                if (options.TryGetValue(COMPRESSION_LEVEL_OPTION, out cplvl) && int.TryParse(cplvl, out tmplvl))
                    cmplevel = Math.Max(Math.Min(9, tmplvl), 0);
                else if (options.TryGetValue(COMPRESSION_LEVEL_OPTION_ALIAS, out cplvl) && int.TryParse(cplvl, out tmplvl))
                    cmplevel = Math.Max(Math.Min(9, tmplvl), 0);

                // Map the reduced API to normal levels:
                //    [9-7] => best 
                //    [6-4] => fast
                //    [3-0] => store
                if (cmplevel >= 7)
                    m_compressionLevel = CompressionLevel.Optimal;
                else if (cmplevel >= 4)
                    m_compressionLevel = CompressionLevel.Fastest;
                else
                    m_compressionLevel = CompressionLevel.NoCompression;

                m_stream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Read);
                m_isWriting = true;
                m_archive = new ZipArchive(
                    m_stream,
                    ZipArchiveMode.Create,
                    false);
                          
                //Size of endheader, taken from SharpCompress ZipWriter
                FlushBufferSize = 8 + 2 + 2 + 4 + 4 + 2 + 0;
            }            
        }

        /// <summary>
        /// Returns all files in the archive, matching the prefix, if any.
        /// </summary>
        /// <param name="prefix">An optional prefix for limiting the files returned</param>
        /// <returns>All files in the archive, matching the prefix, if any</returns>
        public string[] ListFiles(string prefix)
        {
            return ListFilesWithSize(prefix).Select(x => x.Key).ToArray();
        }

        /// <summary>
        /// Returns all files in the archive, matching the prefix, if any.
        /// </summary>
        /// <param name="prefix">An optional prefix for limiting the files returned</param>
        /// <returns>All files in the archive, matching the prefix, if any</returns>
        public IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string prefix)
        {
            foreach (var e in m_archive.Entries)
            {
                if (prefix == null)
                {
                    yield return new KeyValuePair<string, long>(e.FullName, e.Length);
                }
                else
                {
                    if (e.FullName.StartsWith(prefix, Duplicati.Library.Utility.Utility.ClientFilenameStringComparision))
                        yield return new KeyValuePair<string, long>(e.FullName, e.Length);
                    //Some old archives may have been created with windows style paths
                    else if (e.FullName.Replace('\\', '/').StartsWith(prefix, Duplicati.Library.Utility.Utility.ClientFilenameStringComparision))
                        yield return new KeyValuePair<string, long>(e.FullName, e.Length);
                }
            }
        }

        /// <summary>
        /// Locates the file entry
        /// </summary>
        /// <returns>The entry.</returns>
        /// <param name="file">File.</param>
        private ZipArchiveEntry GetEntry(string file)
        {
            var f = m_archive.GetEntry(file);
            if (f != null)
                return f;

            f = m_archive.GetEntry(file.Replace('/', '\\'));
            if (f != null)
                return f;

            return null;
        }
                
        /// <summary>
        /// Returns a stream with data from the given file
        /// </summary>
        /// <param name="file">The file to read the data from</param>
        /// <returns>A stream with data from the given file</returns>
        public System.IO.Stream OpenRead(string file)
        {
            return GetEntry(file).Open();
        }

        /// <summary>
        /// Creates a file in the archive
        /// </summary>
        /// <param name="file">The file to create</param>
        /// <param name="hint">A hint to the compressor as to how compressible the file data is</param>
        /// <param name="lastWrite">The time the file was last written</param>
        /// <returns>A stream with the data to write into the file</returns>
        public System.IO.Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
        {
            if (!m_isWriting)
                throw new InvalidOperationException("Cannot write while reading");

            FlushBufferSize += CENTRAL_HEADER_ENTRY_SIZE + System.Text.Encoding.UTF8.GetByteCount(file);

            var el = m_archive.CreateEntry(file, hint == CompressionHint.Noncompressible ? CompressionLevel.NoCompression : m_compressionLevel);
            el.LastWriteTime = lastWrite;
            return el.Open();
        }

        /// <summary>
        /// Returns a value indicating if the specified file exists
        /// </summary>
        /// <param name="file">The name of the file to examine</param>
        /// <returns>True if the file exists, false otherwise</returns>
        public bool FileExists(string file)
        {
            try
            {
                return GetEntry(file) != null;
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        /// The total size of the archive
        /// </summary>
        public long Size 
        {
            get
            {
                return m_stream.Length;
            }
        }

        /// <summary>
        /// Returns the last modification time for the file
        /// </summary>
        /// <param name="file">The name of the file to examine</param>
        /// <returns>The timestamp on the file</returns>
        public DateTime GetLastWriteTime(string file)
        {
            return GetEntry(file).LastWriteTime.DateTime;
        }

        /// <summary>
        /// Releases all resource used by the <see cref="T:Duplicati.Library.Compression.SystemIOZip"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.Library.Compression.SystemIOZip"/>. The <see cref="Dispose"/> method leaves the
        /// <see cref="T:Duplicati.Library.Compression.SystemIOZip"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.Library.Compression.SystemIOZip"/> so the garbage collector can reclaim the memory
        /// that the <see cref="T:Duplicati.Library.Compression.SystemIOZip"/> was occupying.</remarks>
        public void Dispose()
        {
            if (m_archive != null)
                m_archive.Dispose();
            if (m_stream != null)
                m_stream.Dispose();
        }

        /// <summary>
        /// The size in bytes of the buffer that will be written when flushed
        /// </summary>
        public long FlushBufferSize { get; private set; }

        /// <summary>
        /// The extension that the compression implementation adds to the filename
        /// </summary>
        public string FilenameExtension { get { return "szip"; } }

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
                    new CommandLineArgument(COMPRESSION_LEVEL_OPTION_ALIAS, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionlevelShort, Strings.FileArchiveZip.CompressionlevelLong, DEFAULT_COMPRESSION_LEVEL.ToString(), null, new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"}, Strings.FileArchiveZip.CompressionlevelDeprecated(COMPRESSION_LEVEL_OPTION)),
                });
            }
        }
    }
}
