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
        public readonly string Title;
        /// <summary>
        /// The homepage of the component
        /// </summary>
        public string Url;
        /// <summary>
        /// The license for the component
        /// </summary>
        public readonly string License;
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
