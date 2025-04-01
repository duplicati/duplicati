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

namespace Duplicati.Library.Compression.ZipCompression;

/// <summary>
/// Constants used in the ZipCompression namespace
/// </summary>
internal class Constants
{
    /// <summary>
    /// The size of the local header entry
    /// </summary>
    public const string CannotReadWhileWriting = "Cannot read while writing";
    /// <summary>
    /// The size of the local header entry
    /// </summary>
    public const string CannotWriteWhileReading = "Cannot write while reading";

    /// <summary>
    /// Size of endheader, taken from SharpCompress ZipWriter
    /// </summary>
    public const int END_OF_CENTRAL_DIRECTORY_SIZE = 8 + 2 + 2 + 4 + 4 + 2 + 0;

    /// <summary>
    /// Taken from SharpCompress ZipCentralDirectorEntry.cs
    /// </summary>
    public const int CENTRAL_HEADER_ENTRY_SIZE = 8 + 2 + 2 + 4 + 4 + 4 + 4 + 2 + 2 + 2 + 2 + 2 + 2 + 2 + 4;

    /// <summary>
    /// The size of the extended zip64 header
    /// </summary>
    public const int CENTRAL_HEADER_ENTRY_SIZE_ZIP64_EXTRA = 2 + 2 + 8 + 8 + 8 + 4;
}
