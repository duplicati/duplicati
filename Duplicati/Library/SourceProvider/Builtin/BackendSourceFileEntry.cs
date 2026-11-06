// Copyright (C) 2026, The Duplicati Team
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

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SourceProvider;

/// <summary>
/// Creates a new backend source entry
/// </summary>
/// <param name="parent">The parent backend</param>
/// <param name="path">The path of the entry</param>
/// <param name="isFolder">True if the entry is a folder</param>
/// <param name="isRootEntry">True if the entry is a meta entry</param>
/// <param name="createdUtc">The creation time of the entry</param>
/// <param name="lastModificationUtc">The last modification time of the entry</param>
/// <param name="size">The size of the entry</param>
public class BackendSourceFileEntry(BackendSourceProvider parent, string path, bool isFolder, bool isRootEntry, DateTime createdUtc, DateTime lastModificationUtc, long size)
    : ISourceProviderEntry
{
    /// <inheritdoc/>
    public bool IsFolder => isFolder;

    /// <inheritdoc/>
    public bool IsMetaEntry => false;

    /// <inheritdoc/>
    public bool IsRootEntry => isRootEntry;

    /// <inheritdoc/>
    public DateTime CreatedUtc => createdUtc;

    /// <inheritdoc/>
    public DateTime LastModificationUtc => lastModificationUtc;

    /// <inheritdoc/>
    public string Path => SystemIO.IO_OS.PathCombine(parent?.MountedPath, NormalizePathToLocalSystem(path));

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
    public bool IsBlockDevice => false;

    /// <inheritdoc/>
    public bool IsCharacterDevice => false;

    /// <inheritdoc/>
    public bool IsAlternateStream => false;

    /// <inheritdoc/>
    public string? HardlinkTargetId => null;

    /// <summary>
    /// An async enumerator for the entries
    /// </summary>
    private IAsyncEnumerator<ISourceProviderEntry>? preparedEnumerator = null;

    /// <summary>
    /// A flag to indicate if the prepared enumerator has any entries
    /// </summary>
    private bool preparedEnumeratorAny = false;

    /// <summary>
    /// Prepares the enumerator for this entry
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The prepared enumerator</returns>
    public async Task PrepareEnumerator(CancellationToken cancellationToken)
    {
        if (!isFolder || !isRootEntry)
            throw new InvalidOperationException("PrepareEnumerator can only be called on root folders");

        if (preparedEnumerator != null)
            throw new InvalidOperationException("PrepareEnumerator can only be called once");

        var result = EnumerateInternal(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var prev = Interlocked.Exchange(ref preparedEnumerator, result);
        if (prev != null)
            throw new InvalidOperationException("PrepareEnumerator can only be called once");

        // Advance to the first entry, so we are sure it does not throw exceptions
        preparedEnumeratorAny = await result.MoveNextAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!isFolder)
            throw new InvalidOperationException("Enumerate can only be called on folders");

        if (isRootEntry)
        {
            // If we have a prepared enumerator, consume and use it
            var enumerator = Interlocked.Exchange(ref preparedEnumerator, null);
            if (enumerator != null)
            {
                if (preparedEnumeratorAny)
                {
                    // It has already been advanced, so return the current value
                    yield return enumerator.Current;

                    while (await enumerator.MoveNextAsync())
                        yield return enumerator.Current;
                }

                yield break;
            }
        }

        // Otherwise, enumerate the entries
        await foreach (var entry in EnumerateInternal(cancellationToken).ConfigureAwait(false))
            yield return entry;

    }

    private IAsyncEnumerable<ISourceProviderEntry> EnumerateInternal(CancellationToken cancellationToken)
        => parent.WrappedBackend.ListAsync(NormalizePathTo(path, '/'), cancellationToken)
        // Remove the current and parent folder entries
        .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Name != "." && x.Name != "..")
        // Remove sub-folder entries
        .Where(x => !x.Name[0..^1].Contains('\\') && !x.Name[0..^1].Contains('/'))
        // Convert to source file entries
        .Select(x =>
        {
            var localPath = SystemIO.IO_OS.PathCombine(path, NormalizePathToLocalSystem(x.Name));
            if (x.IsFolder)
                localPath = Util.AppendDirSeparator(localPath);

            return new BackendSourceFileEntry(
                parent,
                localPath,
                x.IsFolder,
                false,
                x.Created,
                x.LastModification,
                x.Size
            );
        });

    /// <inheritdoc/>
    public async Task<bool> FileExists(string path, CancellationToken cancellationToken)
    {
        if (!isFolder)
            throw new InvalidOperationException("FileExists cannot be called on folders");

        try
        {
            var entry = await parent.WrappedBackend.GetEntryAsync(NormalizePathTo(path, '/'), cancellationToken);
            return entry != null && !entry.IsFolder;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        if (isFolder)
            throw new InvalidOperationException("OpenRead can only be called on files");

        if (parent.WrappedBackend is IStreamingBackend streamingBackend && streamingBackend.SupportsStreaming)
        {
            // The download starts on the first read, and is started again from the current
            // position if the connection is lost while the caller is busy elsewhere
            return new ResumingBackendReadStream(streamingBackend, NormalizePathTo(path, '/'), this.Path, size, parent.ResumeAttempts, parent.ResumeDelay, cancellationToken);
        }
        else
        {
            TempFile? tempFile = null;
            TempFileStream? file = null;
            try
            {
                tempFile = new TempFile();
                await parent.WrappedBackend.GetAsync(path, tempFile, cancellationToken);
                file = TempFileStream.Create(tempFile);
                if (file.Length < size)
                    throw new IOException($"The downloaded file for {this.Path} is {file.Length} bytes, but the reported size is {size} bytes");
                return file;
            }
            catch
            {
                file?.Dispose();
                tempFile?.Dispose();
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(isRootEntry
            ? parent.GetRootMetadata()
            : new Dictionary<string, string?>());

    /// <summary>
    /// Normalizes the path, turning backslashes into forward slashes,
    /// or vice versa, depending on the platform
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <returns>The normalized path</returns>
    public static string NormalizePathToLocalSystem(string path)
        => NormalizePathTo(path, System.IO.Path.DirectorySeparatorChar);

    /// <summary>
    /// Normalizes the path, turning backslashes into forward slashes,
    /// or vice versa, depending on the platform
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <returns>The normalized path</returns>
    [return: NotNullIfNotNull("path")]
    public static string? NormalizePathTo(string? path, char separator)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        return path
            .Replace('/', separator)
            .Replace('\\', separator);
    }
}

