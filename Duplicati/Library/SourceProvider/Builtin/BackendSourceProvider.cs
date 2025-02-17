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
/// A source provider that wraps a backend
/// </summary>
/// <param name="backend">The backend to wrap</param>
public class BackendSourceProvider : ISourceProvider
{
    /// <summary>
    /// The wrapped backend
    /// </summary>
    public IBackend WrappedBackend { get; init; }

    /// <summary>
    /// The path key unique for the destination
    /// </summary>
    public readonly string PathKey;

    /// <summary>
    /// Creates a new backend source provider
    /// </summary>
    /// <param name="backend">The backend to wrap</param>
    /// <param name="url">The URL of the backend</param>
    public BackendSourceProvider(IBackend backend, string url)
    {
        WrappedBackend = backend;
        var uri = new Utility.Uri(url);
        // TODO: The username may be provided in other ways to the backend...
        PathKey = $"{uri.Scheme}://{uri.Host}:{uri.Port}/~{uri.Username}/{uri.Path}/";
    }

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
    private class BackendSourceFileEntry(BackendSourceProvider parent, string path, bool isFolder, bool isMetaEntry, DateTime createdUtc, DateTime lastModificationUtc, long size)
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
        public string Path => $"{parent.PathKey}{path}";

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
                    .Select(x => new BackendSourceFileEntry(parent, x.Path, x.IsFolder, false, new DateTime(0), x.LastModificationUtc, x.Size));
            }
            else
            {

                // If we do not support folders, we can only list the root
                if (this.IsMetaEntry)
                    return parent.WrappedBackend.List()
                        .Select(x => new BackendSourceFileEntry(parent, x.Name, x.IsFolder, false, new DateTime(0), x.LastModification, x.Size))
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
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ISourceFileEntry> Enumerate(CancellationToken cancellationToken)
        => new[] { new BackendSourceFileEntry(this, "", true, true, new DateTime(0), new DateTime(0), 0) }.ToAsyncEnumerable();

    /// <inheritdoc/>
    public Task<ISourceFileEntry> GetEntry(string path, bool isFolder, CancellationToken cancellationToken)
        // TODO: If we cache the enumerate result, we could check if the path exists in the cache and return it
        => Task.FromResult<ISourceFileEntry>(new BackendSourceFileEntry(this, path, isFolder, false, new DateTime(0), new DateTime(0), -1));

    /// <inheritdoc/>
    public void Dispose()
    {
        WrappedBackend.Dispose();
        GC.SuppressFinalize(this);
    }
}
