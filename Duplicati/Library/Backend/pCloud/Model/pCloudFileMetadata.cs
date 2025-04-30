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

namespace Duplicati.Library.Backend.pCloud;

/// <summary>
/// Metadata information for a pCloud file
/// </summary>
internal record pCloudFileMetadata
{
    /// <summary>
    /// Always false for file metadata
    /// </summary>
    public bool isfolder { get; init; }

    /// <summary>
    /// Icon type representing the file type
    /// </summary>
    public string icon { get; init; } = string.Empty;

    /// <summary>
    /// Size of the file in bytes
    /// </summary>
    public long size { get; init; }

    /// <summary>
    /// Name of the file including extension
    /// </summary>
    public string name { get; init; } = string.Empty;

    /// <summary>
    /// Category of the file (e.g., 1 for images)
    /// </summary>
    public int category { get; init; }

    /// <summary>
    /// MIME type of the file
    /// </summary>
    public string contenttype { get; init; } = string.Empty;

    /// <summary>
    /// ID of the parent folder containing this file
    /// </summary>
    public ulong parentfolderid { get; init; }

    /// <summary>
    /// Indicates if the file has been deleted
    /// </summary>
    public bool isdeleted { get; init; }

    /// <summary>
    /// Hash value of the file content
    /// </summary>
    public ulong hash { get; init; }

    /// <summary>
    /// Indicates if the file belongs to the authenticated user
    /// </summary>
    public bool ismine { get; init; }

    /// <summary>
    /// Indicates if the file is shared with other users
    /// </summary>
    public bool isshared { get; init; }

    /// <summary>
    /// Unique identifier for the file, prefixed with 'f'
    /// </summary>
    public string id { get; init; } = string.Empty;

    /// <summary>
    /// Height of the image in pixels (only for image files)
    /// </summary>
    public int height { get; init; }

    /// <summary>
    /// Width of the image in pixels (only for image files)
    /// </summary>
    public int width { get; init; }

    /// <summary>
    /// Last modification timestamp in format "Day, DD MMM YYYY HH:MM:SS +0000"
    /// </summary>
    public string modified { get; init; } = string.Empty;

    /// <summary>
    /// Indicates if the file has a thumbnail
    /// </summary>
    public bool thumb { get; init; }

    /// <summary>
    /// Creation timestamp in format "Day, DD MMM YYYY HH:MM:SS +0000"
    /// </summary>
    public string created { get; init; } = string.Empty;

    /// <summary>
    /// Numeric ID of the file
    /// </summary>
    public ulong fileid { get; init; }
    
    /// <summary>
    /// Full path to the file
    /// </summary>
    public string path { get; init; } = string.Empty;
}