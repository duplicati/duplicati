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
