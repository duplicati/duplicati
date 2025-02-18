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
/// Metadata information for a pCloud folder listing, including its contents
/// </summary>
internal record pCloudListFoldersMetadata
{
    /// <summary>
    /// Full path to the folder
    /// </summary>
    public string path { get; init; } = string.Empty;

    /// <summary>
    /// Name of the folder
    /// </summary>
    public string name { get; init; } = string.Empty;

    /// <summary>
    /// Creation timestamp in format "Day, DD MMM YYYY HH:MM:SS +0000"
    /// </summary>
    public string created { get; init; } = string.Empty;

    /// <summary>
    /// Indicates if the folder belongs to the authenticated user
    /// </summary>
    public bool ismine { get; init; }

    /// <summary>
    /// Indicates if the folder has a thumbnail
    /// </summary>
    public bool thumb { get; init; }

    /// <summary>
    /// Last modification timestamp in format "Day, DD MMM YYYY HH:MM:SS +0000"
    /// </summary>
    public string modified { get; init; } = string.Empty;

    /// <summary>
    /// Number of comments on the folder
    /// </summary>
    public int comments { get; init; }

    /// <summary>
    /// Unique identifier for the folder, prefixed with 'd'
    /// </summary>
    public string id { get; init; } = string.Empty;

    /// <summary>
    /// Indicates if the folder is shared with other users
    /// </summary>
    public bool isshared { get; init; }

    /// <summary>
    /// Icon type for the folder
    /// </summary>
    public string icon { get; init; } = string.Empty;

    /// <summary>
    /// Always true for folder metadata
    /// </summary>
    public bool isfolder { get; init; }

    /// <summary>
    /// Numeric ID of this folder
    /// </summary>
    public long folderid { get; init; }

    /// <summary>
    /// List of files and folders contained within this folder
    /// </summary>
    public List<pCloudFolderContent> contents { get; init; } = new();
}