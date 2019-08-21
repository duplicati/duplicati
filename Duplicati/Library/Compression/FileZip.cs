using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Duplicati.Library.Compression
{
    public class FileZip
    {
        public static bool CreateZip(string[] files, string zipFilename)
        {
            using (var archive = ZipFile.Open(zipFilename, ZipArchiveMode.Create))
            {
                foreach (var fPath in files)
                {
                    archive.CreateEntryFromFile(fPath, Path.GetFileName(fPath));
                }
            }

            return File.Exists(zipFilename);
        }
    }
}
