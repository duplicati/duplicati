using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem;

public interface IFilesystem : IDisposable
{
    /// <summary>
    /// Gets the parent partition.
    /// </summary>
    IPartition Partition { get; }

    /// <summary>
    /// Gets the filesystem type.
    /// </summary>
    FileSystemType Type { get; }

    /// <summary>
    /// Gets filesystem-specific metadata. E.g. for journal filesystems, this could be the journal file.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to filesystem-specific metadata.</returns>
    Task<object?> GetFilesystemMetadataAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists top-level files in the filesystem.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to a collection of top-level files.</returns>
    IAsyncEnumerable<IFile> ListFilesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists files in a directory.
    /// </summary>
    /// <param name="directory">The directory to list files from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to a collection of files in the specified directory.</returns>
    /// <throws cref="ArgumentNullException">Thrown if <paramref name="directory"/> is null.</throws>
    /// <throws cref="ArgumentException">Thrown if <paramref name="directory"/> is not a directory.</throws>
    IAsyncEnumerable<IFile> ListFilesAsync(IFile directory, CancellationToken cancellationToken);

    Task<Stream> OpenFileAsync(IFile file, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a file for writing. Creates the file if it doesn't exist.
    /// </summary>
    /// <param name="file">The file to write to. If null, a new file will be created at the specified address.</param>
    /// <param name="address">The address/offset to write to.</param>
    /// <param name="size">The expected size of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream for writing to the file.</returns>
    Task<Stream> CreateFileAsync(IFile? file, long address, long size, CancellationToken cancellationToken);
}

public interface IFile
{
    string? Path { get; }
    long? Address { get; }
    long Size { get; }
    bool IsDirectory { get; }
}

public interface IFileMetadata
{
    byte[]? RawMetadata { get; }

    DateTimeOffset? Created { get; }

    DateTimeOffset? Modified { get; }

    DateTimeOffset? Accessed { get; }
}