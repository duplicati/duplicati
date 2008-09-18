using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Compression
{
    public class Compression
    {
        /// <summary>
        /// Compresses a folder into a single compressed file
        /// </summary>
        /// <param name="folder">The folder to compress</param>
        /// <param name="outputfile">The name of the compressed file</param>
        public static void Compress(string folder, string outputfile, string rootfolder)
        {
            ICSharpCode.SharpZipLib.Zip.ZipFile file = ICSharpCode.SharpZipLib.Zip.ZipFile.Create(outputfile);
            if (!folder.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                folder += System.IO.Path.DirectorySeparatorChar;
            
            file.EntryFactory.NameTransform = new ICSharpCode.SharpZipLib.Zip.ZipNameTransform(rootfolder);

            foreach (string s in Core.Utility.EnumerateFiles(folder))
            {
                file.BeginUpdate();
                file.Add(s);
                file.CommitUpdate();
            }

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
                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));

                using (System.IO.FileStream fs = new System.IO.FileStream(target, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                    Core.Utility.CopyStream(zip.GetInputStream(ze), fs);
            }
        }
    }
}
