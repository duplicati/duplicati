// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using Duplicati.Library.Common.IO;

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
        private static readonly System.Text.RegularExpressions.Regex URL_FILENAME = new System.Text.RegularExpressions.Regex("(homepage.txt)|(download.txt)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        /// <summary>
        /// The regular expression used to find license files
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex LICENSE_FILENAME = new System.Text.RegularExpressions.Regex("license.txt", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        /// <summary>
        /// The regular expression used to find licensedata files
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex LICENSEDATA_FILENAME = new System.Text.RegularExpressions.Regex("licensedata.json", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Reads all license files in the given base folder
        /// </summary>
        /// <param name="basefolder">The folder to search for license files</param>
        /// <returns>A list of licenses</returns>
        public static List<LicenseEntry> ReadLicenses(string basefolder)
        {
            var res = new List<LicenseEntry>();

            var folders = SystemIO.IO_OS.GetDirectories(basefolder);
            Array.Sort(folders);

            foreach (string folder in folders)
            {
                var name = System.IO.Path.GetFileName(folder);
                string urlfile = null;
                string licensefile = null;
                string jsonfile = null;

                foreach (string file in System.IO.Directory.GetFiles(folder))
                    if (URL_FILENAME.IsMatch(System.IO.Path.GetFileName(file)))
                        urlfile = file;
                    else if (LICENSE_FILENAME.IsMatch(System.IO.Path.GetFileName(file)))
                        licensefile = file;
                    else if (LICENSEDATA_FILENAME.IsMatch(System.IO.Path.GetFileName(file)))
                        jsonfile = file;

                if (licensefile != null)
                    res.Add(new LicenseEntry(name, urlfile, licensefile, jsonfile));
            }

            return res;
        }
    }
}
