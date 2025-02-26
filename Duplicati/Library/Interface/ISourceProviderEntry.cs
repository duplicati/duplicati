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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Duplicati.Library.Interface;

/// <summary>
/// Interface for an instance of a file or folder
/// </summary>
public interface ISourceProviderEntry
{
    /// <summary>
    /// True if the entry represents a folder, false otherwise
    /// </summary>
    bool IsFolder { get; }
    /// <summary>
    /// True if the entry is a meta entry, false otherwise
    /// </summary>
    bool IsMetaEntry { get; }
    /// <summary>
    /// True if the entry is the root entry, false otherwise
    /// </summary>
    bool IsRootEntry { get; }
    /// <summary>
    /// The creation time of the file or folder
    /// </summary>
    DateTime CreatedUtc { get; }
    /// <summary>
    /// The time the file or folder was last modified
    /// </summary>
    DateTime LastModificationUtc { get; }
    /// <summary>
    /// The name of the file or folder
    /// </summary>
    string Path { get; }
    /// <summary>
    /// The size of the file or folder
    /// </summary>
    long Size { get; }
    /// <summary>
    /// True if the file is a symlink, false otherwise
    /// </summary>
    bool IsSymlink { get; }
    /// <summary>
    /// The target of a symlink, if the file is a symlink
    /// </summary>
    string? SymlinkTarget { get; }
    /// <summary>
    /// The entry attributes
    /// </summary>
    FileAttributes Attributes { get; }
    /// <summary>
    /// The minor metadata of the file, should be less than 1KB
    /// </summary>
    Dictionary<string, string> MinorMetadata { get; }
    /// <summary>
    /// True if the file is a block device, false otherwise
    /// </summary>
    bool IsBlockDevice { get; }
    /// <summary>
    /// True if the file is a character device, false otherwise
    /// </summary>
    bool IsCharacterDevice { get; }
    /// <summary>
    /// True if the files is an alternate stream, false otherwise
    /// </summary>
    bool IsAlternateStream { get; }
    /// <summary>
    /// The hardlink target id, if the file is a hardlink
    /// </summary>
    string? HardlinkTargetId { get; }

    /// <summary>
    /// Opens the file for reading
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the operation</param>
    /// <returns>A stream that can be read</returns>
    Task<Stream> OpenRead(CancellationToken cancellationToken);

    /// <summary>
    /// Opens the metadata for reading
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the operation</param>
    /// <returns>A stream that can be read</returns>
    Task<Stream?> OpenMetadataRead(CancellationToken cancellationToken);

    /// <summary>
    /// Checks if the specified file exists
    /// </summary>
    /// <param name="filename">The file to check for existence</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation</param>
    /// <returns><c>true</c> if the file exists, <c>false</c> otherwise</returns>
    Task<bool> FileExists(string filename, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the contents of the object, if it is a folder
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the operation</param>
    /// <returns>The contents of the folder</returns>
    IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken);
}
