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

public interface IRestoreDestinationProvider : IDisposable
{
    /// <summary>
    /// The target destination path
    /// </summary>
    string TargetDestination { get; }

    /// <summary>
    /// Initializes the restore destination provider
    /// </summary>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task Initialize(CancellationToken cancel);

    /// <summary>
    /// Finalizes the restore destination provider
    /// </summary>
    /// <param name="progressCallback">A callback to report progress</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task Finalize(Action<double>? progressCallback, CancellationToken cancel);

    /// <summary>
    /// Tests the provider connection
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task Test(CancellationToken cancellationToken);

    /// <summary>
    /// Creates the folder at the given path if it does not exist
    /// </summary>
    /// <param name="path">The path to create</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns><c>true</c> if the folder was created, <c>false</c> if it already existed</returns>
    Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel);

    /// <summary>
    /// Checks if a file exists at the given path
    /// </summary>
    /// <param name="path">The path to check</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns><c>true</c> if the file exists, <c>false</c> otherwise</returns>
    Task<bool> FileExists(string path, CancellationToken cancel);

    /// <summary>
    /// Restores a file to the given path
    /// </summary>
    /// <param name="path">The path to restore to</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>A stream to write the restored data to</returns>
    Task<Stream> OpenWrite(string path, CancellationToken cancel);

    /// <summary>
    /// Opens a file for reading at the given path
    /// </summary>
    /// <param name="path">The path to the file</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>A stream to read the file from</returns>
    Task<Stream> OpenRead(string path, CancellationToken cancel);

    /// <summary>
    /// Opens a file for reading and writing at the given path
    /// </summary>
    /// <param name="path">The path to the file</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>A stream to read and write the file from</returns>
    Task<Stream> OpenReadWrite(string path, CancellationToken cancel);

    /// <summary>
    /// Gets the length of the file at the given path
    /// </summary>
    /// <param name="path">The path to the file</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>The length of the file in bytes</returns>
    Task<long> GetFileLength(string path, CancellationToken cancel);

    /// <summary>
    /// Checks if the file at the given path has the read-only attribute set
    /// </summary>
    /// <param name="path">The path to the file</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns><c>true</c> if the file has the read-only attribute set, <c>false</c> otherwise</returns>
    Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancel);

    /// <summary>
    /// Clears the read-only attribute from the file at the given path
    /// </summary>
    /// <param name="path">The path to the file</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>A task that completes when the attribute is cleared</returns>
    Task ClearReadOnlyAttribute(string path, CancellationToken cancel);

    /// <summary>
    /// Restores metadata to the given path
    /// </summary>
    /// <param name="path">The path to restore to</param>
    /// <param name="metadata">The metadata to restore</param>
    /// <param name="restoreSymlinkMetadata">Whether to restore symlink metadata</param>
    /// <param name="restorePermissions">Whether to restore permissions</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns><c>true</c> if the metadata was restored, <c>false</c> otherwise</returns>
    Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, CancellationToken cancel);

    /// <summary>
    /// Deletes a folder at the given path
    /// </summary>
    /// <param name="path">The path to delete</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>A task that completes when the deletion is done</returns>
    Task DeleteFolder(string path, CancellationToken cancel);

    /// <summary>
    /// Deletes a file at the given path
    /// </summary>
    /// <param name="path">The path to delete</param>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>A task that completes when the deletion is done</returns>
    Task DeleteFile(string path, CancellationToken cancel);


}
