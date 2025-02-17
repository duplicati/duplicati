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

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SourceProvider;

/// <summary>
/// Creates a new backend source entry
/// </summary>
/// <param name="parent">The parent backend</param>
/// <param name="path">The path of the entry</param>
/// <param name="isFolder">True if the entry is a folder</param>
/// <param name="isMetaEntry">True if the entry is a meta entry</param>
/// <param name="createdUtc">The creation time of the entry</param>
/// <param name="lastModificationUtc">The last modification time of the entry</param>
/// <param name="size">The size of the entry</param>
public class BackendSourceFileEntry(BackendSourceProvider parent, string path, bool isFolder, bool isMetaEntry, DateTime createdUtc, DateTime lastModificationUtc, long size)
    : ISourceFileEntry
{
    /// <inheritdoc/>
    public bool IsFolder => isFolder;

    /// <inheritdoc/>
    public bool IsMetaEntry => isMetaEntry;

    /// <inheritdoc/>
    public bool IsRootEntry => isMetaEntry;

    /// <inheritdoc/>
    public DateTime CreatedUtc => createdUtc;

    /// <inheritdoc/>
    public DateTime LastModificationUtc => lastModificationUtc;

    /// <inheritdoc/>
    public string Path => ConcatPaths(parent.PathKey, NormalizePath(path));

    /// <summary>
    /// The local path of the entry
    /// </summary>
    public string LocalPath => path;

    /// <inheritdoc/>
    public long Size => size;
    /// <inheritdoc/>
    public bool IsSymlink => false;

    /// <inheritdoc/>
    public string? SymlinkTarget => null;

    /// <inheritdoc/>
    public FileAttributes Attributes => IsFolder
        ? FileAttributes.Directory
        : FileAttributes.Normal;

    /// <inheritdoc/>
    public Dictionary<string, string> MinorMetadata => new Dictionary<string, string>();

    /// <inheritdoc/>
    public bool IsBlockDevice => false;

    /// <inheritdoc/>
    public bool IsCharacterDevice => false;

    /// <inheritdoc/>
    public bool IsAlternateStream => false;

    /// <inheritdoc/>
    public string? HardlinkTargetId => null;

    /// <inheritdoc/>
    public IAsyncEnumerable<ISourceFileEntry> Enumerate(CancellationToken cancellationToken)
    {
        if (!isFolder)
            throw new InvalidOperationException("Enumerate can only be called on folders");

        if (parent.WrappedBackend is IFolderEnabledBackend folderEnabledBackend)
        {
            return folderEnabledBackend.ListAsync(path, cancellationToken)
                .Select(x => new BackendSourceFileEntry(parent, x.Path, x.IsFolder, false, x.CreatedUtc, x.LastModificationUtc, x.Size));
        }
        else
        {
            // If we do not support folders, we can only list the root
            if (this.IsMetaEntry)
                return parent.WrappedBackend.List()
                    .Select(x => FromFileEntry(parent, "", x))
                    .ToAsyncEnumerable();

            return Array.Empty<ISourceFileEntry>().ToAsyncEnumerable();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> FileExists(string path, CancellationToken cancellationToken)
    {
        if (!isFolder)
            throw new InvalidOperationException("FileExists can only be called on folders");

        if (parent.WrappedBackend is IFolderEnabledBackend folderEnabledBackend)
        {
            try
            {
                var entry = await folderEnabledBackend.GetEntryAsync(path, cancellationToken);
                return entry != null && !entry.IsFolder;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public Task<Stream?> OpenMetadataRead(CancellationToken cancellationToken)
        => Task.FromResult<Stream?>(null);

    /// <inheritdoc/>
    public async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        if (isFolder)
            throw new InvalidOperationException("OpenRead can only be called on files");

        if (parent.WrappedBackend is IStreamingBackend streamingBackend)
        {
            var mapStream = new MapStream();
            mapStream.CopyTask = streamingBackend.GetAsync(this.Path, mapStream, cancellationToken);
            return mapStream;
        }
        else
        {
            TempFileStream? file = null;
            try
            {
                file = TempFileStream.Create();
                await parent.WrappedBackend.GetAsync(this.Path, file.Path, cancellationToken);
                file.Position = 0;
                return file;
            }
            catch
            {
                file?.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Normalizes the path, turning backslashes into forward slashes
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <returns>The normalized path</returns>
    public static string NormalizePath(string path)
    {
        if (System.IO.Path.DirectorySeparatorChar == '/')
            return path;

        if (string.IsNullOrEmpty(path))
            return path;

        return path.Replace('\\', '/');
    }

    /// <summary>
    /// Concatenates two paths
    /// </summary>
    /// <param name="path1">The first path</param>
    /// <param name="path2">The second path</param>
    /// <returns>The concatenated path</returns>
    public static string ConcatPaths(string path1, string path2)
    {
        if (string.IsNullOrEmpty(path1))
            return path2;

        if (string.IsNullOrEmpty(path2))
            return path1;

        if (path1.EndsWith('/') || path1.EndsWith('\\'))
            return path1 + path2;

        return path1 + "/" + path2;
    }

    /// <summary>
    /// Creates a new backend source entry from a file entry
    /// </summary>
    /// <param name="parent">The parent backend</param>
    /// <param name="entry">The file entry</param>
    /// <param name="prefix">The prefix to add to the path</param>
    /// <returns>The new backend source entry</returns>
    public static BackendSourceFileEntry FromFileEntry(BackendSourceProvider parent, string prefix, IFileEntry entry)
        => new BackendSourceFileEntry(parent, ConcatPaths(prefix, entry.Name), entry.IsFolder, false, entry.Created, entry.LastModification, entry.Size);
}

