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

namespace Duplicati.Compression
{
    public class Compression : IDisposable
    {
        private bool m_writing;
        private ICSharpCode.SharpZipLib.Zip.ZipOutputStream m_zipfile;
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
        }

        public long AddFile(string file)
        {
            if (!m_writing)
                throw new InvalidOperationException("Cannot write to a file while reading it");

            ICSharpCode.SharpZipLib.Zip.ZipEntry ze = new ICSharpCode.SharpZipLib.Zip.ZipEntry(file.Substring(m_basename.Length));
            ze.DateTime = System.IO.File.GetLastWriteTime(file);

            m_zipfile.PutNextEntry(ze);

            using(System.IO.FileStream fs = System.IO.File.OpenRead(file))
                Core.Utility.CopyStream(fs, m_zipfile);

            return new System.IO.FileInfo(m_filename).Length;
        }

        public Compression(string zipfile)
        {
            m_writing = false;
            m_filename = zipfile;
            ICSharpCode.SharpZipLib.Zip.ZipFile zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipfile);
        }


        /// <summary>
        /// Compresses a folder into a single compressed file
        /// </summary>
        /// <param name="folder">The folder to compress</param>
        /// <param name="outputfile">The name of the compressed file</param>
        public static void Compress(string folder, string outputfile, string rootfolder)
        {
            ICSharpCode.SharpZipLib.Zip.ZipFile file = ICSharpCode.SharpZipLib.Zip.ZipFile.Create(outputfile);
            folder = Core.Utility.AppendDirSeperator(folder);
            
            file.EntryFactory.NameTransform = new ICSharpCode.SharpZipLib.Zip.ZipNameTransform(rootfolder);

            file.BeginUpdate();
            foreach (string s in Core.Utility.EnumerateFiles(folder))
                file.Add(s);

            foreach (string s in Core.Utility.EnumerateFolders(folder))
                file.AddDirectory(s);

            file.CommitUpdate();

            

            file.Close();
            
        }

        /// <summary>
        /// Decompresses a file into its original directory structure
        /// </summary>
        /// <param name="file">The name of the compressed file</param>
        /// <param name="targetfolder">The folder where the data is extracted to</param>
        public static void Decompress(string file, string targetfolder)
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
