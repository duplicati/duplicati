using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.License
{
    /// <summary>
    /// Simple reader that picks up text file data from a specified folder
    /// </summary>
    public static class LicenseReader
    {
        /// <summary>
        /// The regular expression used to find url files
        /// </summary>
        private static System.Text.RegularExpressions.Regex URL_FILENAME = new System.Text.RegularExpressions.Regex("(homepage.txt)|(download.txt)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        /// <summary>
        /// The regular expression used to find license files
        /// </summary>
        private static System.Text.RegularExpressions.Regex LICENSE_FILENAME = new System.Text.RegularExpressions.Regex("license.txt", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        /// <summary>
        /// Reads all license files in the given base folder
        /// </summary>
        /// <param name="basefolder">The folder to search for license files</param>
        /// <returns>A list of licenses</returns>
        public static List<LicenseEntry> ReadLicenses(string basefolder)
        {
            List<LicenseEntry> res = new List<LicenseEntry>();
            
            string[] folders = System.IO.Directory.GetDirectories(basefolder);
            Array.Sort(folders);

            foreach (string folder in folders)
            {
                string name = System.IO.Path.GetFileName(folder);
                string urlfile = null;
                string licensefile = null;

                foreach (string file in System.IO.Directory.GetFiles(folder))
                    if (URL_FILENAME.IsMatch(System.IO.Path.GetFileName(file)))
                        urlfile = file;
                    else if (LICENSE_FILENAME.IsMatch(System.IO.Path.GetFileName(file)))
                        licensefile = file;

                if (licensefile != null)
                    res.Add(new LicenseEntry(name, urlfile, licensefile));
            }

            return res;
        }
    }
}
