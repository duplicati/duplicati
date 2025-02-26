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

using System.IO.Pipelines;
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
    /// <summary>
    /// The log tag for this instance
    /// </summary>
    private static string LOGTAG = Logging.Log.LogTagFromType<BackendSourceFileEntry>();

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
    public Dictionary<string, string> MinorMetadata => new Dictionary<string, string>();

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
    public Task<Stream?> OpenMetadataRead(CancellationToken cancellationToken)
        => Task.FromResult<Stream?>(null);

    /// <summary>
    /// Helper class for reporting th length of a stream
    /// </summary>
    /// <param name="stream">The stream to wrap</param>
    /// <param name="reportedLength">The length to report</param>
    private class LengthReportingStream(Stream stream, long reportedLength) : OverrideableStream(stream)
    {
        /// <summary>
        /// Track the position
        /// </summary>
        private long position = 0;

        /// <inheritdoc/>
        public override long Length => reportedLength;

        /// <inheritdoc/>
        public override long Position
        {
            get => position;
            set => position = base.Position = value;
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            position += count;
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            position += read;
            return read;
        }

        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await base.ReadAsync(buffer, offset, count, cancellationToken);
            position += read;
            return read;
        }

        /// <inheritdoc/>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await base.WriteAsync(buffer, offset, count, cancellationToken);
            position += count;
        }
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        if (isFolder)
            throw new InvalidOperationException("OpenRead can only be called on files");

        if (parent.WrappedBackend is IStreamingBackend streamingBackend)
        {
            var pipe = new Pipe();

            // Start writing data to the pipe asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await streamingBackend.GetAsync(NormalizePathTo(path, '/'), pipe.Writer.AsStream(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
                }
                finally
                {
                    await pipe.Writer.CompleteAsync().ConfigureAwait(false);
                }
            })
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Logging.Log.WriteWarningMessage(LOGTAG, "ErrorPipingStream", t.Exception, "Error piping stream for {0}", this.Path);
            });

            // Return the readable stream so the caller can read data as it's produced
            return new LengthReportingStream(pipe.Reader.AsStream(), size);
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
    public static string NormalizePathTo(string path, char separator)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        return path
            .Replace('/', separator)
            .Replace('\\', separator);
    }

    /// <summary>
    /// Creates a new backend source entry from a file entry
    /// </summary>
    /// <param name="parent">The parent backend</param>
    /// <param name="entry">The file entry</param>
    /// <param name="prefix">The prefix to add to the path</param>
    /// <returns>The new backend source entry</returns>
    public static BackendSourceFileEntry FromFileEntry(BackendSourceProvider parent, string prefix, IFileEntry entry)
        => new BackendSourceFileEntry(parent, SystemIO.IO_OS.PathCombine(prefix, entry.Name), entry.IsFolder, false, entry.Created, entry.LastModification, entry.Size);
}

