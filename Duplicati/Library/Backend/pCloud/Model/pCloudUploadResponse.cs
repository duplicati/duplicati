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

using System.Collections.Generic;

namespace Duplicati.Library.Backend.pCloud;

/// <summary>
/// Response from pCloud's upload file API endpoint
/// </summary>
internal record pCloudUploadResponse
{
    /// <summary>
    /// Result code from the API call. 0 indicates success
    /// </summary>
    public int result { get; init; }

    /// <summary>
    /// Array of metadata for uploaded files
    /// </summary>
    public List<pCloudFileMetadata> metadata { get; init; } = new();

    /// <summary>
    /// Checksums for uploaded files
    /// </summary>
    public List<pCloudChecksum> checksums { get; init; } = new();

    /// <summary>
    /// List of file IDs for uploaded files
    /// </summary>
    public List<ulong> fileids { get; init; } = new();
}