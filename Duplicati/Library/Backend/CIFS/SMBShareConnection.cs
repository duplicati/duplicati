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
using Duplicati.Library.Backend.CIFS.Model;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Localization.Short;
using SMBLibrary;
using SMBLibrary.Client;
using FileAttributes = SMBLibrary.FileAttributes;

namespace Duplicati.Library.Backend.CIFS;

/// <summary>
/// Class the wraps the SMB connection and file store objects, handling the connection,
/// logon and logoff operations as well as safely disposing resources.
/// </summary>
public class SMBShareConnection : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// SMBConnection client
    /// </summary>
    private readonly SMB2Client _smb2Client = new();

    /// <summary>
    /// Shared fileStore object.
    /// </summary>
    private readonly ISMBFileStore _smbFileStore;

    /// <summary>
    /// Connection parameters
    /// </summary>
    private readonly SMBConnectionParameters _connectionParameters;

    /// <summary>
    /// The semaphore to ensure that only one operation is performed at a time.
    /// </summary>
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private bool _disposed;

    /// <summary>
    /// This constructor will connect to the server and share as specified in the connection parameters.
    /// 
    /// It throws specific exceptions for connection and authentication failures.
    /// </summary>
    /// <param name="connectionParameters">Connection Parameters</param>
    /// <exception cref="UserInformationException">Exception to be displayed to user</exception>
    public SMBShareConnection(SMBConnectionParameters connectionParameters)
    {
        _connectionParameters = connectionParameters;

        if (!_smb2Client.Connect(connectionParameters.ServerName, connectionParameters.TransportType))
            throw new UserInformationException($"{LC.L("Failed to connect to server")} {connectionParameters.ServerName}", "ConnectionError");

        var status = _smb2Client.Login(connectionParameters.AuthDomain ?? "", connectionParameters.AuthUser ?? "", connectionParameters.AuthPassword ?? "");

        if (status != NTStatus.STATUS_SUCCESS)
            throw new UserInformationException($"{LC.L("Failed to authenticate to server")} {connectionParameters.ServerName} with status {status}", "ConnectionError");

        _smbFileStore = _smb2Client.TreeConnect(connectionParameters.ShareName, out status);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new UserInformationException($"{LC.L("Failed to connect to share")} {connectionParameters.ShareName} with status {status}", "ConnectionError");
    }

    /// <summary>
    /// Deletes the file specified in the path specified in the connection parameters.
    /// </summary>
    /// <param name="fileName">Filename to be deleted</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <exception cref="UserInformationException">Exception to be displayed to user</exception>
    public async Task DeleteAsync(string fileName, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            NTStatus status;
            object fileHandle;
            FileStatus fileStatus;
            status = _smbFileStore.CreateFile(out fileHandle, out fileStatus, NormalizeSlashes(Path.Combine(_connectionParameters.Path, fileName)),
                AccessMask.GENERIC_WRITE | AccessMask.DELETE | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal,
                ShareAccess.None,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);

            if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND)
                throw new FileMissingException();

            if (status == NTStatus.STATUS_SUCCESS)
            {
                var fileDispositionInformation = new FileDispositionInformation
                {
                    DeletePending = true
                };
                status = _smbFileStore.SetFileInformation(fileHandle, fileDispositionInformation);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw new UserInformationException($"{LC.L("Failed to delete file on DeleteAsync")} with status {status}", "DeleteFileError");
                status = _smbFileStore.CloseFile(fileHandle);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw new UserInformationException($"{LC.L("Failed to close file on DeleteAsync")} with status {status}", "CloseFileError");
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Create the folder structure specified in the path.
    /// </summary>
    /// <param name="path">Path to create</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <exception cref="UserInformationException">Exception to be displayed to user</exception>
    public async Task CreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Normalize path separators to forward slashes and trim any trailing separators
            string linuxNormalizedPath = path.Replace('/', '\\').TrimEnd('\\');
            string currentPath = "";

            foreach (string part in linuxNormalizedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(part) || part == ".")
                    continue;

                currentPath = currentPath.Length == 0 ? part : $"{currentPath}\\{part}";
                object? fileHandle = null;
                try
                {
                    NTStatus status = _smbFileStore.CreateFile(
                        out fileHandle,
                        out FileStatus fileStatus,
                        currentPath,
                        AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                        FileAttributes.Normal,
                        ShareAccess.None,
                        CreateDisposition.FILE_CREATE,
                        CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                        null
                    );
                    if (status != NTStatus.STATUS_SUCCESS &&
                        status != NTStatus.STATUS_OBJECT_NAME_COLLISION) // Ignore if directory already exists
                        throw new UserInformationException($"{LC.L("Failed to create directory")} {currentPath} with status{status}", "CreateDirectoryError");
                }
                finally
                {
                    if (fileHandle != null)
                    {
                        var status = _smbFileStore.CloseFile(fileHandle);
                        if (status != NTStatus.STATUS_SUCCESS)
                            throw new UserInformationException($"{LC.L("Failed to close file handle on CreateFolderAsync")} with status {status.ToString()}", "HandleCloseError");
                    }
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Lists the folder contents of the share and path specified in the connection parameters.
    /// </summary>
    /// <param name="path">Path to list</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <exception cref="UserInformationException">Exception to be displayed to user</exception>
    public async Task<List<IFileEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        object? directoryHandle = null;
        FileStatus fileStatus;
        try
        {
            try
            {
                var status = _smbFileStore.CreateFile(
                    out directoryHandle,
                    out fileStatus,
                    NormalizeSlashes(path),
                    AccessMask.GENERIC_READ,
                    FileAttributes.Directory,
                    ShareAccess.Read | ShareAccess.Write,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_DIRECTORY_FILE,
                    null);

                if (status != NTStatus.STATUS_SUCCESS && fileStatus != FileStatus.FILE_OPENED)
                    if (status == NTStatus.STATUS_OBJECT_PATH_NOT_FOUND || status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND)
                        throw new FolderMissingException();
                    else
                        throw new UserInformationException($"{LC.L("Failed to open directory")} {NormalizeSlashes(path)} with status {status}", "DirectoryOpenError");

                List<QueryDirectoryFileInformation> fileList;
                status = _smbFileStore.QueryDirectory(
                    out fileList,
                    directoryHandle,
                    "*",
                    FileInformationClass.FileDirectoryInformation);

                if (status != NTStatus.STATUS_NO_MORE_FILES)
                    throw new UserInformationException($"{LC.L("Failed to query directory contents")} with status {status}", "DirectoryQueryError");

                return
                [
                    ..fileList
                        .OfType<FileDirectoryInformation>()
                        .Select(info => new FileEntry(
                            info.FileName,
                            info.EndOfFile,
                            info.LastAccessTime,
                            info.LastWriteTime)
                        {
                            IsFolder = info.FileAttributes == FileAttributes.Directory,
                            Created = info.CreationTime
                        })
                        .ToList()
                ];
            }
            finally
            {
                if (directoryHandle != null)
                {
                    var status = _smbFileStore.CloseFile(directoryHandle);
                    if (status != NTStatus.STATUS_SUCCESS)
                        throw new UserInformationException($"{LC.L("Failed to close directory handle on ListAsync")} with status {status.ToString()}", "HandleCloseError");
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }

    }

    /// <summary>
    /// Read a file from source and write it to the destination stream.
    /// </summary>
    /// <param name="filename">Filename to be read</param>
    /// <param name="destinationStream">Destination stream to write contents read from source</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    public async Task GetAsync(string filename, Stream destinationStream, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            object? fileHandle;
            FileStatus fileStatus;
            NTStatus status = _smbFileStore.CreateFile(out fileHandle, out fileStatus,
                NormalizeSlashes(Path.Combine(_connectionParameters.Path, filename)),
                AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE, FileAttributes.Normal, ShareAccess.Read,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);

            if (status == NTStatus.STATUS_SUCCESS || fileStatus != FileStatus.FILE_DOES_NOT_EXIST)
            {
                byte[] data;
                long bytesRead = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Use the provided read buffer size if set, otherwise use the protocol negotiated maximum. Never exceed the negotiated maximum.
                    int readBufferSize = Math.Min(_connectionParameters.ReadBufferSize ?? (int)_smb2Client.MaxReadSize, (int)_smb2Client.MaxReadSize);
                    status = _smbFileStore.ReadFile(out data, fileHandle, bytesRead, readBufferSize);
                    if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_END_OF_FILE)
                        throw new UserInformationException($"{LC.L("Failed to read file on GetAsync")} {filename} with status {status.ToString()}", "FileReadError");

                    if (status == NTStatus.STATUS_END_OF_FILE || data.Length == 0)
                        break;

                    bytesRead += data.Length;
                    await destinationStream.WriteAsync(data, 0, data.Length, cancellationToken);
                }

                await destinationStream.FlushAsync(cancellationToken);

                if (fileHandle != null)
                {
                    status = _smbFileStore.CloseFile(fileHandle);
                    if (status != NTStatus.STATUS_SUCCESS)
                        throw new UserInformationException($"{LC.L("Failed to close file handle on GetAsync")} {filename} with status {status.ToString()}", "HandleCloseError");
                }
            }
            else if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND)
                throw new FileMissingException($"{LC.L("The requested file does not exist")} {filename}");
            else if (status == NTStatus.STATUS_OBJECT_PATH_NOT_FOUND)
                throw new FolderMissingException();
            else
                throw new UserInformationException(
                    $"{LC.L("Failed to open file with error")} {filename} with status {status.ToString()}",
                    "FileOpenError");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Writes file in the remote share and path specified in the connection parameters reading
    /// from the sourceStream provided.
    /// </summary>
    /// <param name="filename"></param>
    /// <param name="sourceStream"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="Exception"></exception>
    public async Task PutAsync(string filename, Stream sourceStream, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            object fileHandle;
            FileStatus fileStatus;
            NTStatus status = _smbFileStore.CreateFile(out fileHandle, out fileStatus,
                NormalizeSlashes(Path.Combine(_connectionParameters.Path, filename)),
                AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                FileAttributes.Normal, ShareAccess.None,
                CreateDisposition.FILE_SUPERSEDE,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);
            if (status == NTStatus.STATUS_SUCCESS)
            {
                // Use the provided write buffer size if set, otherwise use the protocol negotiated maximum. Never exceed the negotiated maximum.
                byte[] buffer = new byte[Math.Min(_connectionParameters.WriteBufferSize ?? (int)_smb2Client.MaxWriteSize, _smb2Client.MaxWriteSize)];
                int bytesRead;
                int numberOfBytesWritten;
                int offset = 0;
                while (!cancellationToken.IsCancellationRequested && sourceStream.Position < sourceStream.Length)
                {
                    bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken);
                    if (bytesRead == 0)
                        break;
                    status = _smbFileStore.WriteFile(out numberOfBytesWritten, fileHandle, offset, buffer.Take(bytesRead).ToArray());
                    offset += numberOfBytesWritten;
                    if (numberOfBytesWritten != bytesRead)
                        throw new UserInformationException(LC.L("Failed to write to file, difference between bytes read and bytes written"), "HandleWriteError");
                    if (status != NTStatus.STATUS_SUCCESS)
                        throw new UserInformationException($"{LC.L("Failed to write file on Putasync")} {filename} with status {status.ToString()}", "HandleWriteError");
                }
                if (fileHandle != null)
                {
                    status = _smbFileStore.CloseFile(fileHandle);
                    if (status != NTStatus.STATUS_SUCCESS)
                        throw new UserInformationException($"{LC.L("Failed to close file handle on PutAsync")} {filename} with status {status.ToString()}", "HandleCloseError");
                }
            }
            else
            {
                throw new UserInformationException($"{LC.L("Failed to create file for writing")} {filename} with status {status.ToString()}", "FileCreateError");
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Normalizes paths to use backslashes (for Windows shares compatibility) and removes trailing slashes.
    ///
    /// Samba deals with \ and / in paths, but Windows shares require backslashes.
    /// </summary>
    /// <param name="path">Path to be normalized</param>
    /// <returns></returns>
    private string NormalizeSlashes(string path)
    {
        return path.Replace('/', '\\').TrimEnd('\\');
    }

    /// <summary>
    /// Synchronously dispose the resources.
    /// </summary>
    public void Dispose()
    {
        DisposeResourcesAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Async dispose implementation
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeResourcesAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Inner dispose implementation
    /// </summary>
    private async ValueTask DisposeResourcesAsync()
    {
        if (_disposed) return;

        await _semaphore.WaitAsync(); // Ensure no operations are in progress
        try
        {
            if (_smbFileStore != null)
            {
                try
                {
                    await Task.Run(() => _smbFileStore.Disconnect());
                }
                catch { /* Can be safely ignored */ }
            }

            if (_smb2Client != null)
            {
                try
                {
                    await Task.Run(() => _smb2Client.Logoff());
                    await Task.Run(() => _smb2Client.Disconnect());
                }
                catch { /* Can be safely ignored */ }
            }

            _semaphore.Dispose();
        }
        finally
        {
            _disposed = true;
        }
    }
}
