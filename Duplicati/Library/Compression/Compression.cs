#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.Library.Compression
{
    public class Compression : IDisposable
    {
        private bool m_writing;
        private ICSharpCode.SharpZipLib.Zip.ZipOutputStream m_zipfile;
        private ICSharpCode.SharpZipLib.Zip.ZipEntryFactory m_zef;
        private System.IO.FileStream m_filestream;
        private string m_filename;
        private string m_basename;

        public Compression(string basefolder, string zipfile)
        {
            m_basename = basefolder;
            m_writing = true;
            m_filename = zipfile;
            m_filestream = System.IO.File.Create(zipfile);
            m_zipfile = new ICSharpCode.SharpZipLib.Zip.ZipOutputStream(m_filestream);
            m_zef = new ICSharpCode.SharpZipLib.Zip.ZipEntryFactory();
            m_zef.NameTransform = new ICSharpCode.SharpZipLib.Zip.ZipNameTransform(m_basename);
        }

        public void AddFolder(string folder)
        {
            ICSharpCode.SharpZipLib.Zip.ZipEntry ze = m_zef.MakeDirectoryEntry(folder, true);
            m_zipfile.PutNextEntry(ze);
        }

        public long AddFile(string file)
        {
            if (!m_writing)
                throw new InvalidOperationException("Cannot write to a file while reading it");

            ICSharpCode.SharpZipLib.Zip.ZipEntry ze = m_zef.MakeFileEntry(file, true);
            m_zipfile.PutNextEntry(ze);

            using(System.IO.FileStream fs = System.IO.File.OpenRead(file))
                Core.Utility.CopyStream(fs, m_zipfile);

            return new System.IO.FileInfo(m_filename).Length;
        }

        public long Size 
        {
            get 
            {
                m_zipfile.Flush();
                return m_zipfile.Length;
            }
        }


        public System.IO.Stream AddStream(string filename)
        {
            if (!m_writing)
                throw new InvalidOperationException("Cannot write to a file while reading it");


            ICSharpCode.SharpZipLib.Zip.ZipEntry ze;
            try
            {
                //This call breaks on long filenames...
                ze = m_zef.MakeFileEntry(filename, true);
                ze.DateTime = System.IO.File.GetLastWriteTime(filename);
            }
            catch(System.IO.PathTooLongException)
            {
                ze = new ICSharpCode.SharpZipLib.Zip.ZipEntry(m_zef.NameTransform.TransformFile(filename));
                //Does not work when the path is too long
                //ze.DateTime = System.IO.File.GetLastWriteTime(filename);
            }

            m_zipfile.PutNextEntry(ze);
            return m_zipfile;
        }

        public Compression(string zipfile)
        {
            m_writing = false;
            m_filename = zipfile;
            ICSharpCode.SharpZipLib.Zip.ZipFile zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipfile);
        }

        /// <summary>
        /// Compresses a list of files into a single archive
        /// </summary>
        /// <param name="files">The files to add</param>
        /// <param name="folders">The folders to create</param>
        /// <param name="outputfile">The file to write the output to</param>
        /// <param name="rootfolder">The root folder, used to calculate relative paths</param>
        public static void Compress(List<string> files, List<string> folders, string outputfile, string rootfolder)
        {
            using (new Logging.Timer("Compression of " + files.Count.ToString() + " files and " + (folders == null ? 0 : folders.Count).ToString() + " folders"))
            {
                ICSharpCode.SharpZipLib.Zip.ZipEntryFactory zef = new ICSharpCode.SharpZipLib.Zip.ZipEntryFactory();

                rootfolder = Core.Utility.AppendDirSeperator(rootfolder);
                using (System.IO.FileStream fs = System.IO.File.Create(outputfile))
                using (ICSharpCode.SharpZipLib.Zip.ZipOutputStream zipfile = new ICSharpCode.SharpZipLib.Zip.ZipOutputStream(fs))
                {
                    zipfile.SetLevel(9);

                    if (folders != null)
                        foreach (string f in folders)
                        {
                            ICSharpCode.SharpZipLib.Zip.ZipEntry e = zef.MakeDirectoryEntry(f.Substring(rootfolder.Length), false);
                            zipfile.PutNextEntry(e);
                        }

                    foreach (string f in files)
                    {
                        ICSharpCode.SharpZipLib.Zip.ZipEntry e = zef.MakeFileEntry(f.Substring(rootfolder.Length), false);
                        e.DateTime = System.IO.File.GetLastWriteTime(f);
                        zipfile.PutNextEntry(e);

                        using (System.IO.FileStream ffs = System.IO.File.OpenRead(f))
                            Core.Utility.CopyStream(ffs, zipfile);
                    }

                    zipfile.Close();
                }
            }
        }

        /// <summary>
        /// Compresses a folder into a single compressed file
        /// </summary>
        /// <param name="folder">The folder to compress</param>
        /// <param name="outputfile">The name of the compressed file</param>
        public static void Compress(string folder, string outputfile, string rootfolder)
        {
            Compress(Core.Utility.EnumerateFiles(folder), Core.Utility.EnumerateFolders(folder), outputfile, rootfolder);
        }

        /// <summary>
        /// Decompresses a file into its original directory structure
        /// </summary>
        /// <param name="file">The name of the compressed file</param>
        /// <param name="targetfolder">The folder where the data is extracted to</param>
        public static void Decompress(string file, string targetfolder)
        {
            using (new Logging.Timer("Decompression of " + file + " (" + new System.IO.FileInfo(file).Length.ToString() + ")"))
            {
                ICSharpCode.SharpZipLib.Zip.ZipFile zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(file);
                foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry ze in zip)
                {
                    string target = System.IO.Path.Combine(targetfolder, ze.Name);
                    if (ze.IsDirectory)
                    {
                        if (!System.IO.Directory.Exists(target))
                            System.IO.Directory.CreateDirectory(target);
                    }
                    else
                    {
                        if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));

                        using (System.IO.FileStream fs = new System.IO.FileStream(target, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                            Core.Utility.CopyStream(zip.GetInputStream(ze), fs);
                    }
                }
                zip.Close();
            }
        }

        /// <summary>
        /// Returns the contents of a compressed file
        /// </summary>
        /// <param name="file">The compressed file</param>
        /// <returns>The list of files withing</returns>
        public static List<string> ListFiles(string file)
        {
            List<string> results = new List<string>();
            using (new Logging.Timer("Listing " + file + " (" + new System.IO.FileInfo(file).Length.ToString() + ")"))
            {
                ICSharpCode.SharpZipLib.Zip.ZipFile zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(file);
                foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry ze in zip)
                {
                    if (ze.IsDirectory)
                        results.Add(Core.Utility.AppendDirSeperator(ze.Name));
                    else
                        results.Add(ze.Name);
                }
                zip.Close();
            }

            return results;
        }

        public static List<string> GetAllLines(string file, string path)
        {
            List<string> res = new List<string>();
            using (new Logging.Timer("GetAllLines " + file + ", path " + path + " (" + new System.IO.FileInfo(file).Length.ToString() + ")"))
            {
                ICSharpCode.SharpZipLib.Zip.ZipFile zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(file);
                ICSharpCode.SharpZipLib.Zip.ZipEntry ze = zip.GetEntry(path);
                if (ze != null)

                    using (System.IO.StreamReader sr = new System.IO.StreamReader(zip.GetInputStream(ze)))
                        while(!sr.EndOfStream)
                            res.Add(sr.ReadLine());

                zip.Close();
            }

            return res;
        }

        #region IDisposable Members

        public void Dispose()
        {
            try
            {
                if (m_zipfile != null)
                    m_zipfile.Close();
                m_zipfile = null;
            }
            catch
            {
            }
        }

        #endregion
    }
}
