#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

//The compile flag SHARPZIPLIBWORKS can be set if #ZipLib is able to update archives without corrupting them

namespace Duplicati.Library.Compression
{
    /// <summary>
    /// An abstraction of a zip archive as a FileArchive.
    /// Due to a very unfortunate Zip implementation, the archive is either read or write, never both
    /// </summary>
    public class FileArchiveZip : Core.IFileArchive
    {
        /// <summary>
        /// The archive used for read access
        /// </summary>
        private ICSharpCode.SharpZipLib.Zip.ZipFile m_zip;
#if !SHARPZIPLIBWORKS
        private ICSharpCode.SharpZipLib.Zip.ZipOutputStream m_stream;
        private FileArchiveZip(ICSharpCode.SharpZipLib.Zip.ZipOutputStream stream)
        {
            m_stream = stream;
        }
#endif


        public static FileArchiveZip CreateArchive(string filename)
        {
#if SHARPZIPLIBWORKS
            FileArchiveZip z = new FileArchiveZip(ICSharpCode.SharpZipLib.Zip.ZipFile.Create(filename));
            z.m_zip.BeginUpdate();
            return z;
#else
            return new FileArchiveZip(new ICSharpCode.SharpZipLib.Zip.ZipOutputStream(System.IO.File.Create(filename)));
#endif
        }

        private FileArchiveZip(ICSharpCode.SharpZipLib.Zip.ZipFile zip)
        {
            m_zip = zip;
        }

        public FileArchiveZip(string file)
            : this(new ICSharpCode.SharpZipLib.Zip.ZipFile(file))
        {
        }

        private string PathToFilesystem(string path)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                return path.Replace('/', System.IO.Path.DirectorySeparatorChar);
            else
                return path;
        }

        private string PathFromFilesystem(string path)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                return path.Replace(System.IO.Path.DirectorySeparatorChar, '/');
            else
                return path;
        }

        #region IFileArchive Members

        public string[] ListFiles(string prefix)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception("Cannot read data while writing");
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

        public string[] ListDirectories(string prefix)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception("Cannot read data while writing");
#endif

            List<string> results = new List<string>();
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry ze in m_zip)
            {
                string name = PathToFilesystem(ze.Name);
                if (prefix == null || name.StartsWith(prefix))
                {
                    if (ze.IsDirectory)
                        results.Add(Core.Utility.AppendDirSeperator(name));
                }
            }

            return results.ToArray();
        }

        public string[] ListEntries(string prefix)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception("Cannot read data while writing");
#endif

            List<string> results = new List<string>();
            foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry ze in m_zip)
            {
                string name = PathToFilesystem(ze.Name);
                if (prefix == null || name.StartsWith(prefix))
                {
                    if (ze.IsDirectory)
                        results.Add(Core.Utility.AppendDirSeperator(name));
                    else
                        results.Add(name);
                }
            }

            return results.ToArray();
        }

        public byte[] ReadAllBytes(string file)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            using(System.IO.Stream s = OpenRead(file))
            {
                Core.Utility.CopyStream(s, ms);
                return ms.ToArray();
            }
        }

        public string[] ReadAllLines(string file)
        {
            List<string> lines = new List<string>();
            using (System.IO.StreamReader sr = new System.IO.StreamReader(OpenRead(file)))
                while (!sr.EndOfStream)
                    lines.Add(sr.ReadLine());
            return lines.ToArray();
        }

        public System.IO.Stream OpenRead(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception("Cannot read data while writing");
#endif

            ICSharpCode.SharpZipLib.Zip.ZipEntry ze = GetEntry(file);
            
            if (ze == null)
                return null;
            else
                return m_zip.GetInputStream(ze);
        }

        public System.IO.Stream OpenWrite(string file)
        {
            return CreateFile(file);
        }

        public void WriteAllBytes(string file, byte[] data)
        {
            using (System.IO.Stream s = CreateFile(file))
                s.Write(data, 0, data.Length);
        }

        public void WriteAllLines(string file, string[] data)
        {
            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(CreateFile(file)))
                foreach (string s in data)
                    sw.WriteLine(s);
        }

        private ICSharpCode.SharpZipLib.Zip.ZipEntry GetEntry(string file)
        {
            ICSharpCode.SharpZipLib.Zip.ZipEntry ze = m_zip.GetEntry(PathFromFilesystem(file));
            
            if (ze == null) //Grrr... The zip library has a pretty relaxed take on seperators
                ze = m_zip.GetEntry(PathToFilesystem(file));

            return ze;
        }

        public void DeleteFile(string file)
        {
#if !SHARPZIPLIBWORKS
            throw new MissingMethodException("Zip does not support deleting");
#else
            if (FileExists(file))
                m_zip.Delete(GetEntry(file));
#endif
            
        }

        public System.IO.Stream CreateFile(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_stream == null)
                throw new Exception("Cannot write while reading");
#endif

#if SHARPZIPLIBWORKS
            if (FileExists(file))
                DeleteFile(file);

            return new StreamWrapper(new Duplicati.Library.Core.TempFile(), PathFromFilesystem(file), m_zip);
#else
            m_stream.PutNextEntry(new ICSharpCode.SharpZipLib.Zip.ZipEntry(PathFromFilesystem(file)));
            return new StreamWrapper2(m_stream);
#endif
        }

        public void DeleteDirectory(string file)
        {
#if SHARPZIPLIBWORKS
            if (DirectoryExists(file))
                m_zip.Delete(GetEntry(file));
#else
            throw new MissingMethodException("Zip does not support deleting");
#endif
        }

        public void AddDirectory(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_stream == null)
                throw new Exception("Cannot write while reading");
#endif
            m_zip.AddDirectory(PathFromFilesystem(file));
        }

        public bool FileExists(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception("Cannot read data while writing");
#endif
            return GetEntry(file) != null && GetEntry(file).IsFile;
        }

        public bool DirectoryExists(string file)
        {
#if !SHARPZIPLIBWORKS
            if (m_zip == null)
                throw new Exception("Cannot read data while writing");
#endif
            return GetEntry(file) != null && GetEntry(file).IsDirectory;
        }

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

#if !SHARPZIPLIBWORKS

        private class StreamWrapper2 : Core.OverrideableStream
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
