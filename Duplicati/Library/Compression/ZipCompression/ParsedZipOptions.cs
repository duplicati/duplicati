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

using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;

namespace Duplicati.Library.Compression.ZipCompression;

/// <summary>
/// The zip options parsed from the command line
/// </summary>
/// <param name="DeflateCompressionLevel">The compression level to use</param>
/// <param name="CompressionType">The compression type to use</param>
/// <param name="UseZip64">Whether to use zip64 extensions</param>
/// <param name="UnittestMode">Flag indicating if unittest mode is enabled</param>
public sealed record ParsedZipOptions(
    CompressionLevel DeflateCompressionLevel,
    CompressionType CompressionType,
    bool UseZip64,
    bool UnittestMode
);
