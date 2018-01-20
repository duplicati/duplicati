#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
    /// Class that represents a single license entry
    /// </summary>
    public class LicenseEntry
    {
        /// <summary>
        /// The component title
        /// </summary>
        public string Title;
        /// <summary>
        /// The homepage of the component
        /// </summary>
        public string Url;
        /// <summary>
        /// The license for the component
        /// </summary>
        public string License;
        /// <summary>
        /// The json data
        /// </summary>
        public string Jsondata;

        /// <summary>
        /// Constructs a new license entry
        /// </summary>
        /// <param name="title">The component title</param>
        /// <param name="urlfile">The homepage of the component</param>
        /// <param name="licensefile">The license for the component</param>
        public LicenseEntry(string title, string urlfile, string licensefile, string jsonfile)
        {
            Title = title;
            if (!string.IsNullOrEmpty(urlfile) && System.IO.File.Exists(urlfile))
                Url = System.IO.File.ReadAllText(urlfile).Trim();
            License = System.IO.File.ReadAllText(licensefile);
            if (License.IndexOf("\r\n", StringComparison.Ordinal) < 0)
                License = License.Replace("\n", "\r\n").Replace("\r", "\r\n");
            if (Environment.NewLine != "\r\n")
                License = License.Replace("\r\n", Environment.NewLine);
            if (jsonfile != null)
                Jsondata = System.IO.File.ReadAllText(jsonfile);

        }

        /// <summary>
        /// Returns the title of the item as the identification
        /// </summary>
        /// <returns>The item title</returns>
        public override string ToString()
        {
            return Title;
        }
    }
}
