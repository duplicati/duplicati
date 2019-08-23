using System;
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

        public static List<string> UnZip(string zipFilename)
        {
            var unzippedFiles = new List<string>();

            using (var archive = ZipFile.Open(zipFilename, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destinationPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(zipFilename), entry.FullName));
                    entry.ExtractToFile(destinationPath);
                    unzippedFiles.Add(destinationPath);
                }
            }

            return unzippedFiles;
        }

    }
}
