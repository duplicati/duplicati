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
using System.IO;

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// An installer entry, describing an architecture specific package
/// </summary>
/// <param name="RemoteUrls">The urls for the updater payload</param>
/// <param name="Length">The length of the payload</param>
/// <param name="MD5">The MD5 hash of the payload</param>
/// <param name="SHA256">The SHA256 hash of the payload</param>
/// <param name="PackageTypeId">The package type id</param>
public record PackageEntry(
    string[] RemoteUrls,
    long Length,
    string MD5,
    string SHA256,
    string PackageTypeId
)
{
    /// <summary>
    /// Gets the name of the package file, formatted as a valid local filename
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
