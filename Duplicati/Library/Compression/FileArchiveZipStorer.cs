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
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Compression
{
    public class FileArchiveZipStorer : ICompression
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
        /// A flag indicating if the archive is being written
        /// </summary>
        private readonly bool m_isWriting;
        /// <summary>
        /// The stream representing the archive
        /// </summary>
        private readonly Stream m_stream;
        /// <summary>
        /// The archive instance
        /// </summary>
        private readonly ZipStorer m_archive;
        /// <summary>
        /// A lookup table with all stream entries
        /// </summary>
        private Dictionary<string, ZipStorer.ZipFileEntry> m_entryDict = null;

        /// <summary>
        /// Default constructor, used to read file extension and supported commands
        /// </summary>
        public FileArchiveZipStorer()
        {
        }

        /// <summary>
        /// Constructs a new zip instance.
        /// If the file exists and has a non-zero length we read it,
        /// otherwise we create a new archive.
        /// </summary>
        /// <param name="filename">The name of the file to read or write</param>
        /// <param name="options">The options passed on the commandline</param>
        public FileArchiveZipStorer(string filename, Dictionary<string, string> options)
        {
            if (string.IsNullOrEmpty(filename) && filename.Trim().Length == 0)
                throw new ArgumentException("filename");

            if (File.Exists(filename) && new FileInfo(filename).Length > 0)
            {
                m_isWriting = false;
                m_stream = new System.IO.FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
                m_archive = ZipStorer.Open(m_stream, FileAccess.Read);
            }
            else
            {
                m_isWriting = true;
                m_stream = new System.IO.FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Read);
                m_archive = ZipStorer.Create(m_stream, string.Empty);

                //Size of endheader, taken from SharpCompress ZipWriter
                FlushBufferSize = 8 + 2 + 2 + 4 + 4 + 2 + 0;
            }
        }
        /// <summary>
        /// Gets the filename extension used by the compression module
        /// </summary>
        public string FilenameExtension { get { return "zzip"; } }
        /// <summary>
        /// Gets a friendly name for the compression module
        /// </summary>
        public string DisplayName { get { return Strings.FileArchiveZip.DisplayName; } }
        /// <summary>
        /// Gets a description of the compression module
        /// </summary>
        public string Description { get { return Strings.FileArchiveZip.Description; } }

        /// <summary>
        /// The size of the buffers that will be written when the file is completed
        /// </summary>
        public long FlushBufferSize { get; private set; }

        /// <summary>
        /// Gets the current size of the stream.
        /// </summary>
        public long Size
        {
            get
            {
                return m_stream.Length;
            }
        }

        /// <summary>
        /// Gets the supported commands.
        /// </summary>
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>();
            }
        }

        /// <summary>
        /// Creates a file in the archive and returns a writeable stream
        /// </summary>
        /// <param name="file">The name of the file to create</param>
        /// <param name="hint">A hint to the compressor as to how compressible the file data is</param>
        /// <param name="lastWrite">The time the file was last written</param>
        /// <returns>A writeable stream for the file contents</returns>
        public Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
        {
            return m_archive.Add(
                hint == CompressionHint.Noncompressible ? CompressionLevel.NoCompression : CompressionLevel.Optimal,
                file,
                lastWrite.ToUniversalTime(),
                string.Empty
            );
                            
            /*return new SharedStream(x =>
                m_archive.AddStream(
                    hint == CompressionHint.Noncompressible ? ZipStorer.Compression.Store : ZipStorer.Compression.Deflate,
                    file,
                    x,
                    lastWrite.ToUniversalTime(),
                    string.Empty
                )
            );*/
        }

        /// <summary>
        /// Releases all resource used by the <see cref="T:Duplicati.Library.Compression.FileArchiveZipStorer"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.Library.Compression.FileArchiveZipStorer"/>. The <see cref="Dispose"/> method leaves
        /// the <see cref="T:Duplicati.Library.Compression.FileArchiveZipStorer"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.Library.Compression.FileArchiveZipStorer"/> so the garbage collector can reclaim the
        /// memory that the <see cref="T:Duplicati.Library.Compression.FileArchiveZipStorer"/> was occupying.</remarks>
        public void Dispose()
        {
            if (m_archive != null)
                m_archive.Dispose();
            if (m_stream != null)
                m_stream.Dispose();
        }

        /// <summary>
        /// Loads all entries from the archive into the lookup table
        /// </summary>
        private void LoadEntryDict()
        {
            if (m_entryDict == null)
            {
                m_entryDict = new Dictionary<string, ZipStorer.ZipFileEntry>(Duplicati.Library.Utility.Utility.ClientFilenameStringComparer);
                foreach (var en in m_archive.ReadCentralDir())
                    m_entryDict[en.FilenameInZip] = en;
            }
        }

        /// <summary>
        /// Gets the entry with the given name.
        /// </summary>
        /// <returns>The entry matching the filename.</returns>
        /// <param name="file">The name of the file to locate.</param>
        private ZipStorer.ZipFileEntry GetEntry(string file)
        {
            LoadEntryDict();

            ZipStorer.ZipFileEntry ze;
            if (!m_entryDict.TryGetValue(file, out ze))
                m_entryDict.TryGetValue(file.Replace('/', '\\'), out ze);
            
            return ze;
            
        }

        /// <summary>
        /// Gets a value indicating if the file exists
        /// </summary>
        /// <returns><c>true</c>, if exists was filed, <c>false</c> otherwise.</returns>
        /// <param name="file">The name of the file to locate.</param>
        public bool FileExists(string file)
        {
            var ze = GetEntry(file);
            return !string.IsNullOrWhiteSpace(ze.FilenameInZip);
        }

        /// <summary>
        /// Gets the last write time of the given file.
        /// </summary>
        /// <returns>The last write time of the file.</returns>
        /// <param name="file">The name of the file to locate.</param>
        public DateTime GetLastWriteTime(string file)
        {
            var ze = GetEntry(file);
            if (string.IsNullOrWhiteSpace(ze.FilenameInZip))
                throw new FileNotFoundException(file);
            
            return ze.ModifyTime.ToLocalTime();
        }

        /// <summary>
        /// Lists the files in the archive
        /// </summary>
        /// <returns>The list of files.</returns>
        /// <param name="prefix">The prefix to use in filtering.</param>
        public string[] ListFiles(string prefix)
        {
            LoadEntryDict();
            return m_entryDict.Values.Select(x => x.FilenameInZip).ToArray();
        }

        /// <summary>
        /// Lists the files in the archive with their sizes
        /// </summary>
        /// <returns>The files in the archive with the sizes.</returns>
        /// <param name="prefix">The prefix to use in filtering.</param>
        public IEnumerable<KeyValuePair<string, long>> ListFilesWithSize(string prefix)
        {
            LoadEntryDict();
            return m_entryDict.Values.Select(x => new KeyValuePair<string, long>(x.FilenameInZip, x.FileSize)).ToArray();
        }

        /// <summary>
        /// Opens an file for reading
        /// </summary>
        /// <param name="file">The name of the file to open</param>
        /// <returns>A stream with the file contents</returns>
        public Stream OpenRead(string file)
        {
            var ze = GetEntry(file);
            if (string.IsNullOrWhiteSpace(ze.FilenameInZip))
                throw new FileNotFoundException(file);

            return m_archive.Extract(ze);
            /*var st = new SharedStream();
            Task.Run(() =>
            {
                using (st) 
                    m_archive.ExtractFile(ze, st);
            });
            return st;*/
        }
    }
}
