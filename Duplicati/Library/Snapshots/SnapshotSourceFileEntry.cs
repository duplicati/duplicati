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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Snapshots;


/// <summary>
/// A snapshot-based source file entry
/// </summary>
/// <param name="service">The snapshot service</param>
/// <param name="path">The path of the file</param>
/// <param name="isFolder">True if the entry is a folder, false otherwise</param>
/// <param name="isRoot">True if the entry is the root entry, false otherwise</param>
public class SnapshotSourceFileEntry(ISnapshotService service, string path, bool isFolder, bool isRoot) : ISourceProviderEntry
{
    /// <summary>
    /// The symlink target
    /// </summary>
    private string? symlinkTarget;
    /// <summary>
    /// The file attributes
    /// </summary>
    private FileAttributes? attributes;
    /// <summary>
    /// Gets the symlink target or null if the entry is not a symlink
    /// </summary>
    public string? SymlinkTarget
    {
        get
        {
            if (symlinkTarget != null)
                return symlinkTarget;

            if (!service.IsSymlink(path, Attributes))
                return null;

            return symlinkTarget = service.GetSymlinkTarget(path);
        }
    }

    /// <summary>
    /// True if the entry is a symlink, false otherwise
    /// </summary>
    public bool IsSymlink => SymlinkTarget != null;

    /// <summary>
    /// Gets the entry attributes
    /// </summary>
    public FileAttributes Attributes
    {
        get
        {
            if (attributes != null)
                return attributes.Value;

            attributes = service.GetAttributes(path);
            return attributes.Value;
        }
    }

    /// <summary>
    /// Gets the minor metadata for the file
    /// </summary>
    public Dictionary<string, string> MinorMetadata => service.GetMetadata(path, IsSymlink);

    /// <summary>
    /// Gets a value indicating if the entry is a block device
    /// </summary>
    public bool IsBlockDevice => service.IsBlockDevice(path);

    /// <summary>
    /// Gets a value indicating if the entry is a character device
    /// </summary>
    public bool IsCharacterDevice => false;

    /// <summary>
    /// Gets a value indicating if the entry is an alternate stream
    /// </summary>
    public bool IsAlternateStream => false;

    /// <summary>
    /// Gets the hardlink target ID, or null if the entry is not a hardlink
    /// </summary>
    public string? HardlinkTargetId => service.HardlinkTargetID(path);

    /// <inheritdoc/>
    public bool IsFolder => isFolder;

    /// <inheritdoc/>
    public bool IsMetaEntry => false;
    /// <inheritdoc/>
    public bool IsRootEntry => isRoot;

    /// <inheritdoc/>
    public DateTime CreatedUtc => service.GetCreationTimeUtc(path);

    /// <inheritdoc/>
    public DateTime LastModificationUtc => service.GetLastWriteTimeUtc(path);

    /// <inheritdoc/>
    public string Path => path;

    /// <inheritdoc/>
    public long Size => IsFolder ? -1 : service.GetFileSize(path);

    /// <inheritdoc/>
    public Task<Stream> OpenRead(CancellationToken cancellationToken) => service.OpenReadAsync(path, cancellationToken);

    /// <inheritdoc/>
    public Task<Stream?> OpenMetadataRead(CancellationToken cancellationToken) => Task.FromResult<Stream?>(new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(MinorMetadata)));
    /// <inheritdoc/>
    public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken) => service.EnumerateFilesystemEntries(this).ToAsyncEnumerable();
    /// <inheritdoc/>
    public Task<bool> FileExists(string filename, CancellationToken cancellationToken) => Task.FromResult(service.FileExists(System.IO.Path.Combine(path, filename)));
}