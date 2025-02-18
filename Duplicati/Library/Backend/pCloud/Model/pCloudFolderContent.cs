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

#nullable enable
namespace Duplicati.Library.Backend.pCloud;

/// <summary>
/// Content item (file or folder) within a pCloud folder
/// </summary>
internal record pCloudFolderContent
{
    /// <summary>
    /// Full path to the item
    /// </summary>
    public string path { get; init; } = string.Empty;

    /// <summary>
    /// Name of the file or folder
    /// </summary>
    public string name { get; init; } = string.Empty;

    /// <summary>
    /// Creation timestamp in format "Day, DD MMM YYYY HH:MM:SS +0000"
    /// </summary>
    public string created { get; init; } = string.Empty;

    /// <summary>
    /// Indicates if the item belongs to the authenticated user
    /// </summary>
    public bool ismine { get; init; }

    /// <summary>
    /// Indicates if the item has a thumbnail
    /// </summary>
    public bool thumb { get; init; }

    /// <summary>
    /// Last modification timestamp in format "Day, DD MMM YYYY HH:MM:SS +0000"
    /// </summary>
    public string modified { get; init; } = string.Empty;

    /// <summary>
    /// Number of comments on the item
    /// </summary>
    public int comments { get; init; }

    /// <summary>
    /// Unique identifier for the item, prefixed with 'd' for folders or 'f' for files
    /// </summary>
    public string id { get; init; } = string.Empty;

    /// <summary>
    /// Indicates if the item is shared with other users
    /// </summary>
    public bool isshared { get; init; }

    /// <summary>
    /// Icon type for the item
    /// </summary>
    public string icon { get; init; } = string.Empty;

    /// <summary>
    /// True if the item is a folder, false if it's a file
    /// </summary>
    public bool isfolder { get; init; }

    /// <summary>
    /// Folder ID if the item is a folder (null otherwise)
    /// </summary>
    public ulong folderid { get; init; }

    /// <summary>
    /// File ID if the item is a file (null otherwise)
    /// </summary>
    public ulong? fileid { get; init; }

    /// <summary>
    /// Size in bytes if the item is a file (null otherwise)
    /// </summary>
    public long? size { get; init; }

    /// <summary>
    /// Content type if the item is a file (null otherwise)
    /// </summary>
    public string? contenttype { get; init; }

    /// <summary>
    /// ID of the parent folder containing this item
    /// </summary>
    public long parentfolderid { get; init; }
}