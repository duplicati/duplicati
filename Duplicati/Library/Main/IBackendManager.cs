using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using IFileEntry = Duplicati.Library.Interface.IFileEntry;

#nullable enable

namespace Duplicati.Library.Main;

/// <summary>
/// Interface for the backend manager
/// </summary>
internal interface IBackendManager : IDisposable
{
    /// <summary>
    /// Uploads a block volume to the backend, including an optional index volume
    /// </summary>
    /// <param name="blockVolume">The block volume to upload</param>
    /// <param name="indexVolume">The index volume to upload, if any</param>
    /// <param name="indexVolumeFinished">The action to call when the index volume is finished</param>
    /// <param name="waitForComplete">Whether to wait for the upload to complete</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task PutAsync(VolumeWriterBase blockVolume, IndexVolumeWriter? indexVolume, Action? indexVolumeFinished, bool waitForComplete, CancellationToken cancelToken);

    /// <summary>
    /// Uploads a file to the backend without encryption
    /// </summary>
    /// <param name="remotename">The name of the file to upload to</param>
    /// <param name="tempFile">The file to upload</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task PutVerificationFileAsync(string remotename, TempFile tempFile, CancellationToken cancelToken);

    /// <summary>
    /// Waits for the backend queue to be empty and flushes any pending messages to the database
    /// </summary>
    /// <param name="database">The database to write pending messages to</param>
    /// <param name="transaction">The transaction to use, if any</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task WaitForEmptyAsync(LocalDatabase database, IDbTransaction? transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the files on the backend
    /// </summary>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An enumerable of file entries</returns>
    Task<IEnumerable<IFileEntry>> ListAsync(CancellationToken cancelToken);

    /// <summary>
    /// Decrypts the given file and returns the decrypted file
    /// </summary>
    /// <param name="volume">The file to decrypt</param>
    /// <param name="volume_name">The name of the file. Used for detecting encryption algorithm if not specified in options or if it differs from the options</param>
    /// <param name="options">The Duplicati options</param>
    /// <returns>The decrypted file</returns>
    TempFile DecryptFile(TempFile volume, string volume_name, Options options);

    /// <summary>
    /// Deletes a file on the backend
    /// </summary>
    /// <param name="remotename">The name of the file to delete</param>
    /// <param name="size">The size of the file to delete, or -1 if not known</param>
    /// <param name="waitForComplete">Whether to wait for the delete to complete</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task DeleteAsync(string remotename, long size, bool waitForComplete, CancellationToken cancelToken);

    /// <summary>
    /// Gets the quota information for the backend
    /// </summary>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The quota information, or null if not available or disabled</returns>
    Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken);

    /// <summary>
    /// Gets a file with the hash and size
    /// </summary>
    /// <param name="remotename">The name of the file to get</param>
    /// <param name="hash">The hash of the file to get, or <c>null</c> if not known</param>
    /// <param name="size">The size of the file to get, or -1 if not known</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The file, hash, and size</returns>
    Task<(TempFile File, string Hash, long Size)> GetWithInfoAsync(string remotename, string hash, long size, CancellationToken cancelToken);

    /// <summary>
    /// Gets a file from the backend
    /// </summary>
    /// <param name="remotename">The name of the file to get</param>
    /// <param name="hash">The hash of the file to get, or <c>null</c> if not known</param>
    /// <param name="size">The size of the file to get, or -1 if not known</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The downloaded file</returns>
    Task<TempFile> GetAsync(string remotename, string hash, long size, CancellationToken cancelToken);

    /// <summary>
    /// Gets a file from the backend without decrypting it
    /// </summary>
    /// <param name="remotename">The name of the remote volume</param>
    /// <param name="hash">The hash of the volume</param>
    /// <param name="size">The size of the volume</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The downloaded file</returns>
    Task<TempFile> GetDirectAsync(string remotename, string hash, long size, CancellationToken cancelToken);

    /// <summary>
    /// Performs a download of the files specified, with pre-fetch to overlap the download and processing
    /// </summary>
    /// <param name="volumes">The volumes to download</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The downloaded files, hash, size, and name</returns>
    IAsyncEnumerable<(TempFile File, string Hash, long Size, string Name)> GetFilesOverlappedAsync(IEnumerable<IRemoteVolume> volumes, CancellationToken cancelToken);

    /// <summary>
    /// Gets the size of the last read operation
    /// </summary>
    long LastReadSize { get; }

    /// <summary>
    /// Gets the size of the last write operation
    /// </summary>
    long LastWriteSize { get; }

}
