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

namespace Duplicati.Library.Compression
{
    public class Compression
    {
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
    }
}
