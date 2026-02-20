using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem;

/// <summary>
/// Interface for filesystem access on a partition.
/// Provides methods for listing, reading, and writing files within a filesystem.
/// </summary>
internal interface IFilesystem : IDisposable
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

    /// <summary>
    /// Opens a stream for reading a file.
    /// </summary>
    /// <param name="file">The file to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream for reading the file contents.</returns>
    Task<Stream> OpenReadStreamAsync(IFile file, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stream for writing a file.
    /// </summary>
    /// <param name="file">The file to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream for writing the file contents.</returns>
    Task<Stream> OpenWriteStreamAsync(IFile file, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stream for reading and writing a file.
    /// </summary>
    /// <param name="file">The file to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream for reading and writing the file contents.</returns>
    Task<Stream> OpenReadWriteStreamAsync(IFile file, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the length of a file.
    /// </summary>
    /// <param name="file">The file to get the length of.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file length in bytes.</returns>
    Task<long> GetFileLengthAsync(IFile file, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stream for reading a file by path.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream for reading the file contents.</returns>
    Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stream for writing a file by path.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream for writing the file contents.</returns>
    Task<Stream> OpenWriteStreamAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stream for reading and writing a file by path.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream for reading and writing the file contents.</returns>
    Task<Stream> OpenReadWriteStreamAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the length of a file by path.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file length in bytes.</returns>
    Task<long> GetFileLengthAsync(string path, CancellationToken cancellationToken);
}

/// <summary>
/// Represents a file or directory within a filesystem.
/// </summary>
public interface IFile
{
    /// <summary>
    /// Gets the file path.
    /// </summary>
    string? Path { get; }

    /// <summary>
    /// Gets the physical address on disk (for raw block access).
    /// </summary>
    long? Address { get; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    long Size { get; }

    /// <summary>
    /// Gets a value indicating whether this is a directory.
    /// </summary>
    bool IsDirectory { get; }
}

/// <summary>
/// Represents file metadata including timestamps.
/// </summary>
public interface IFileMetadata
{
    /// <summary>
    /// Gets the raw metadata bytes from the filesystem.
    /// </summary>
    byte[]? RawMetadata { get; }

    /// <summary>
    /// Gets the creation time.
    /// </summary>
    DateTimeOffset? Created { get; }

    /// <summary>
    /// Gets the last modification time.
    /// </summary>
    DateTimeOffset? Modified { get; }

    /// <summary>
    /// Gets the last access time.
    /// </summary>
    DateTimeOffset? Accessed { get; }
}