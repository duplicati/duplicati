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
using System.Text;
using Duplicati.Library.Interface;

//The compile flag SHARPZIPLIBWORKS can be set if #ZipLib is able to update archives without corrupting them

namespace Duplicati.Library.Compression
{
    /// <summary>
    /// An abstraction of a zip archive as a FileArchive.
    /// Due to a very unfortunate Zip implementation, the archive is either read or write, never both
    /// </summary>
    public class FileArchiveZip : ICompression
    {
        /// <summary>
        /// The commandline option for toggling the compression level
        /// </summary>
        private const string COMPRESSION_LEVEL_OPTION = "compression-level";

        /// <summary>
        /// The default compression level
        /// </summary>
        private const int DEFAULT_COMPRESSION_LEVEL = 9;

        /// <summary>
        /// The archive used for read access
        /// </summary>
        private ICSharpCode.SharpZipLib.Zip.ZipFile m_zip;

        /// <summary>
        /// The size of the central header, calculated from the files added
        /// </summary>
        private long m_headersize = 0;

        /// <summary>
        /// The encoding used for filenames.
        /// Used to calculate the size of the Central Header.
        /// </summary>
        private static readonly Encoding _filenameEncoding = Encoding.UTF8; 
        //NOTE: We should use ICSharpCode.SharpZipLib.Zip.ZipConstants.DefaultCodePage),
        // but since we set the bit flag Unicode, it becomes UTF-8 as defined in
        // the Zip 6.3.0 format description, Appendix D - Language Encoding (EFS)

#if !SHARPZIPLIBWORKS
        /// <summary>
        /// We need an output stream because we cannot update an existing file
        /// </summary>
        private ICSharpCode.SharpZipLib.Zip.ZipOutputStream m_stream;
#endif

        /// <summary>
        /// Default constructor, used to read file extension and supported commands
        /// </summary>
        public FileArchiveZip()
        {
        }

        /// <summary>
        /// Constructs a new zip instance.
        /// If the file exists and has a non-zero length we read it,
        /// otherwise we create a new archive.
        /// Note that due to a bug with updating archives, an archive cannot be both read and write.
        /// </summary>
        /// <param name="file">The name of the file to read or write</param>
        /// <param name="options">The options passed on the commandline</param>
        public FileArchiveZip(string file, Dictionary<string, string> options)
        {
            if (!System.IO.File.Exists(file) || new System.IO.FileInfo(file).Length == 0)
            {
#if SHARPZIPLIBWORKS
                m_zip = new FileArchiveZip(ICSharpCode.SharpZipLib.Zip.ZipFile.Create(filename));
                m_zip.BeginUpdate();
#else
                m_stream = new ICSharpCode.SharpZipLib.Zip.ZipOutputStream(System.IO.File.Create(file));
#endif
                int compressionLevel = DEFAULT_COMPRESSION_LEVEL;
                int tmplvl;
                string cplvl = null;

                if (options.TryGetValue(COMPRESSION_LEVEL_OPTION, out cplvl) && int.TryParse(cplvl, out tmplvl))
                    compressionLevel = Math.Max(Math.Min(9, tmplvl), 0);

                m_stream.SetLevel(compressionLevel);
            }
            else
            {
                m_zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(file);
            }
        }

        /// <summary>
        /// Converts a zip file path to the format used by the local filesystem.
        /// Internally all files are stored in the archive use / as directory separator.
        /// </summary>
        /// <param name="path">The path to convert in internal format</param>
        /// <returns>The path in filesystem format</returns>
        private string PathToFilesystem(string path)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                return path.Replace('/', System.IO.Path.DirectorySeparatorChar);
            else
                return path;
        }

        /// <summary>
        /// Converts a file system path to the internal format.
        /// Internally all files are stored in the archive use / as directory separator.
        /// </summary>
        /// <param name="path">The path to convert in filesystem format</param>
        /// <returns>The path in the internal format</returns>
        private string PathFromFilesystem(string path)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                return path.Replace(System.IO.Path.DirectorySeparatorChar, '/');
            else
                return path;
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
            get { return new List<ICommandLineArgument>( new ICommandLineArgument[] {
                new CommandLineArgument(COMPRESSION_LEVEL_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.FileArchiveZip.CompressionlevelShort, Strings.FileArchiveZip.CompressionlevelLong, DEFAULT_COMPRESSION_LEVEL.ToString(), null, new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"})
            } ); }
        }

        /// <summary>
        /// Returns a list of files matching the given prefix
        /// </summary>
        /// <param name="prefix">The prefix to match</param>
        /// <returns>A list of files matching the prefix</returns>
        public string[] ListFiles(string prefix)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception(Strings.FileArchiveZip.AttemptReadWhileWritingError);
#endif
            List<string> results = new List<string>();
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry ze in m_zip)
            {
                string name = PathToFilesystem(ze.Name);
                if (prefix == null || name.StartsWith(prefix))
                {
                    if (!ze.IsDirectory)
                        results.Add(name);
                }
            }

            return results.ToArray();
        }


        /// <summary>
        /// Returns a list of folders matching the given prefix
        /// </summary>
        /// <param name="prefix">The prefix to match</param>
        /// <returns>A list of folders matching the prefix</returns>
        public string[] ListDirectories(string prefix)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception(Strings.FileArchiveZip.AttemptReadWhileWritingError);
#endif

            List<string> results = new List<string>();
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry ze in m_zip)
            {
                string name = PathToFilesystem(ze.Name);
                if (prefix == null || name.StartsWith(prefix))
                {
                    if (ze.IsDirectory)
                        results.Add(Utility.Utility.AppendDirSeparator(name));
                }
            }

            return results.ToArray();
        }

        /// <summary>
        /// Returns a list of entries matching the given prefix
        /// </summary>
        /// <param name="prefix">The prefix to match</param>
        /// <returns>A list of entries matching the prefix</returns>
        public string[] ListEntries(string prefix)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception(Strings.FileArchiveZip.AttemptReadWhileWritingError);
#endif

            List<string> results = new List<string>();
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry ze in m_zip)
            {
                string name = PathToFilesystem(ze.Name);
                if (prefix == null || name.StartsWith(prefix))
                {
                    if (ze.IsDirectory)
                        results.Add(Utility.Utility.AppendDirSeparator(name));
                    else
                        results.Add(name);
                }
            }

            return results.ToArray();
        }

        /// <summary>
        /// Reads all bytes from a file and returns it as an array
        /// </summary>
        /// <param name="file">The name of the file to read</param>
        /// <returns>The contents of the file as a byte array</returns>
        public byte[] ReadAllBytes(string file)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            using(System.IO.Stream s = OpenRead(file))
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
            List<string> lines = new List<string>();
            using (System.IO.StreamReader sr = new System.IO.StreamReader(OpenRead(file), System.Text.Encoding.UTF8, true))
                while (!sr.EndOfStream)
                    lines.Add(sr.ReadLine());
            return lines.ToArray();
        }

        /// <summary>
        /// Opens an file for reading
        /// </summary>
        /// <param name="file">The name of the file to open</param>
        /// <returns>A stream with the file contents</returns>
        public System.IO.Stream OpenRead(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception(Strings.FileArchiveZip.AttemptReadWhileWritingError);
#endif

            ICSharpCode.SharpZipLib.Zip.ZipEntry ze = GetEntry(file);
            if (ze == null)
                return null;
            else if (ze.Size == 0)
                return new ZerobyteStream();
            else
                return m_zip.GetInputStream(ze);
        }

        /// <summary>
        /// Opens a file for writing
        /// </summary>
        /// <param name="file">The name of the file to write</param>
        /// <returns>A stream that can be updated with file contents</returns>
        public System.IO.Stream OpenWrite(string file)
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
            using (System.IO.Stream s = CreateFile(file))
                s.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Writes the given lines into the file
        /// </summary>
        /// <param name="file">The name of the file</param>
        /// <param name="data">The lines to write</param>
        public void WriteAllLines(string file, string[] data)
        {
            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(CreateFile(file), System.Text.Encoding.UTF8))
                foreach (string s in data)
                    sw.WriteLine(s);
        }

        /// <summary>
        /// Internal function that returns a ZipEntry for a filename, or null if no such file exists
        /// </summary>
        /// <param name="file">The name of the file to find</param>
        /// <returns>The ZipEntry for the file or null if no such file was found</returns>
        private ICSharpCode.SharpZipLib.Zip.ZipEntry GetEntry(string file)
        {
            ICSharpCode.SharpZipLib.Zip.ZipEntry ze = m_zip.GetEntry(PathFromFilesystem(file));
            
            if (ze == null) //Grrr... The zip library has a pretty relaxed take on separators
                ze = m_zip.GetEntry(PathToFilesystem(file));

            return ze;
        }

        /// <summary>
        /// Deletes a file from the archive
        /// </summary>
        /// <param name="file">The name of the file to delete</param>
        public void DeleteFile(string file)
        {
#if !SHARPZIPLIBWORKS
            throw new MissingMethodException(Strings.FileArchiveZip.DeleteUnsupportedError);
#else
            if (FileExists(file))
                m_zip.Delete(GetEntry(file));
#endif
            
        }

        /// <summary>
        /// Creates a file in the archive and returns a writeable stream
        /// </summary>
        /// <param name="file">The name of the file to create</param>
        /// <returns>A writeable stream for the file contents</returns>
        public System.IO.Stream CreateFile(string file)
        {
            return CreateFile(file, DateTime.Now);
        }

        /// <summary>
        /// Creates a file in the archive and returns a writeable stream
        /// </summary>
        /// <param name="file">The name of the file to create</param>
        /// <param name="lastWrite">The time the file was last written</param>
        /// <returns>A writeable stream for the file contents</returns>
        public System.IO.Stream CreateFile(string file, DateTime lastWrite)
        {
#if !SHARPZIPLIBWORKS
            if (m_stream == null)
                throw new Exception(Strings.FileArchiveZip.AttemptWriteWhileReadingError);
#endif
            string entryname = PathFromFilesystem(file);
            m_headersize += 46 + 24 + _filenameEncoding.GetByteCount(entryname);
#if SHARPZIPLIBWORKS
            if (FileExists(file))
                DeleteFile(file);

            return new StreamWrapper(new Duplicati.Library.Core.TempFile(), entryname, m_zip);
#else
            ICSharpCode.SharpZipLib.Zip.ZipEntry ze = new ICSharpCode.SharpZipLib.Zip.ZipEntry(entryname);
            ze.DateTime = lastWrite;
            
            //Encode filenames as unicode, we do this for all files, to avoid codepage issues
            ze.Flags |= (int)ICSharpCode.SharpZipLib.Zip.GeneralBitFlags.UnicodeText;
            
            m_stream.PutNextEntry(ze);
            return new StreamWrapper2(m_stream);
#endif
        }

        /// <summary>
        /// Deletes a folder from the archive
        /// </summary>
        /// <param name="file">The name of the folder to delete</param>
        public void DeleteDirectory(string file)
        {
#if SHARPZIPLIBWORKS
            if (DirectoryExists(file))
                m_zip.Delete(GetEntry(file));
#else
            throw new MissingMethodException(Strings.FileArchiveZip.DeleteUnsupportedError);
#endif
        }

        /// <summary>
        /// Adds a folder to the archive
        /// </summary>
        /// <param name="file">The name of the folder to create</param>
        public void AddDirectory(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_stream == null)
                throw new Exception(Strings.FileArchiveZip.AttemptWriteWhileReadingError);
#endif
            m_zip.AddDirectory(PathFromFilesystem(file));
        }

        /// <summary>
        /// Returns a value that indicates if the file exists
        /// </summary>
        /// <param name="file">The name of the file to test existence for</param>
        /// <returns>True if the file exists, false otherwise</returns>
        public bool FileExists(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception(Strings.FileArchiveZip.AttemptReadWhileWritingError);
#endif
            return GetEntry(file) != null && GetEntry(file).IsFile;
        }

        /// <summary>
        /// Returns a value that indicates if the folder exists
        /// </summary>
        /// <param name="file">The name of the folder to test existence for</param>
        /// <returns>True if the folder exists, false otherwise</returns>
        public bool DirectoryExists(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception(Strings.FileArchiveZip.AttemptReadWhileWritingError);
#endif
            return GetEntry(file) != null && GetEntry(file).IsDirectory;
        }

        /// <summary>
        /// Gets the current size of the archive
        /// </summary>
        public long Size
        {
            get
            {
#if !SHARPZIPLIBWORKS
                if (m_zip == null)
                {
                    m_stream.Flush();
                    return m_stream.Length;
                }
                else
#endif
                    return new System.IO.FileInfo(m_zip.Name).Length;
            }
        }


        /// <summary>
        /// Gets the last write time for a file
        /// </summary>
        /// <param name="file">The name of the file to query</param>
        /// <returns>The last write time for the file</returns>
        public DateTime GetLastWriteTime(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception(Strings.FileArchiveZip.AttemptWriteWhileReadingError);
#endif
            if (GetEntry(file) != null)
                return GetEntry(file).DateTime;
            else 
                throw new Exception(string.Format(Strings.FileArchiveZip.FileNotFoundError, file));
        }

        /// <summary>
        /// The size of the current unflushed buffer
        /// </summary>
        public long FlushBufferSize 
        { 
            get 
            {
                //The 1200 is a safety margin, I have seen up to 1050 bytes extra, but unfortunately not found the source
                return m_headersize + (long)ICSharpCode.SharpZipLib.Zip.ZipConstants.CentralHeaderBaseSize + 1200; 
            } 
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_zip != null)
            {
                if (m_zip.IsUpdating)
                    m_zip.CommitUpdate();
                m_zip.Close();

#if SHARPZIPLIBWORKS
                //This breaks, because the updates are not flushed correctly
                m_zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(m_zip.Name);
                m_zip.Close();
#endif            
            }
            m_zip = null;

#if !SHARPZIPLIBWORKS
            if (m_stream != null)
            {
                m_stream.Flush();
                m_stream.Finish();
                m_stream.Close();
            }
#endif
        }

        #endregion

        /// <summary>
        /// A stream that is empty. Used to extract zero byte files, because SharpZipLib throws exceptions when reading such files
        /// </summary>
        private class ZerobyteStream : System.IO.Stream
        {
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override void Flush() { }
            public override long Length { get { return 0; } }
            public override long Position { get { return 0; } set { if (value != 0) throw new ArgumentOutOfRangeException(); } }
            public override int Read(byte[] buffer, int offset, int count) { return 0; }
            public override long Seek(long offset, System.IO.SeekOrigin origin) { throw new NotImplementedException(); }
            public override void SetLength(long value) { throw new NotImplementedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotImplementedException(); }
        }

#if !SHARPZIPLIBWORKS

        /// <summary>
        /// Stream wrapper to prevent closing the base stream when disposing the entry stream
        /// </summary>
        private class StreamWrapper2 : Utility.OverrideableStream
        {
            public StreamWrapper2(System.IO.Stream stream)
                : base(stream)
            {
            }

            protected override void Dispose(bool disposing)
            {
                ((ICSharpCode.SharpZipLib.Zip.ZipOutputStream)m_basestream).CloseEntry();
                //Don't dispose the stream!
                //base.Dispose(disposing);
            }
        }
#else

        private class StreamWrapper : Core.OverrideableStream, ICSharpCode.SharpZipLib.Zip.IStaticDataSource
        {
            private ICSharpCode.SharpZipLib.Zip.ZipFile m_zip;
            private string m_filename;
            private Core.TempFile m_file;

            public StreamWrapper(Core.TempFile file, string filename, ICSharpCode.SharpZipLib.Zip.ZipFile zip)
                : base(System.IO.File.Create(file))
            {
                m_file = file;
                m_zip = zip;
                m_filename = filename;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (m_zip != null)
                    {
                        m_basestream.Flush();
                        m_basestream.Position = 0;

                        if (!m_zip.IsUpdating)
                            m_zip.BeginUpdate();
                        m_zip.Add(this, m_filename);
                        m_file.Dispose();
                    }
                    m_zip = null;
                }
                base.Dispose(disposing);
            }

            #region IStaticDataSource Members

            public System.IO.Stream GetSource()
            {
                return m_basestream;
            }

            #endregion
        }
#endif
    }
}
