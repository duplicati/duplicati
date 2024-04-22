// Copyright (C) 2024, The Duplicati Team
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
using System.IO;

namespace Duplicati.Library.AutoUpdater
{
    /// <summary>
    /// An installer entry, describing an architecture specific package
    /// </summary>
    public class PackageEntry
    {
        /// <summary>
        /// The urls for the updater payload
        /// </summary>
        public string[] RemoteUrls;
        /// <summary>
        /// The length of the payload
        /// </summary>
        public long Length;
        /// <summary>
        /// The MD5 hash of the payload
        /// </summary>
        public string MD5;
        /// <summary>
        /// The SHA256 hash of the payload
        /// </summary>
        public string SHA256;
        /// <summary>
        /// The package type id
        /// </summary>
        public string PackageTypeId;

        /// <summary>
        /// Gets the name of the package file
        /// </summary>
        /// <returns>The filename of the package</returns>
        public string GetFilename()
        {
            var guess = Path.GetFileName(new Uri(RemoteUrls[0]).LocalPath);
            if (string.IsNullOrWhiteSpace(guess))
                guess = "update.bin";

            return guess;
        }
    }
}