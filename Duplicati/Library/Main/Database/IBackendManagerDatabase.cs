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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Duplicati.Library.Main.Database;

/// <summary>
/// Interface for database operations used by the backend manager.
/// </summary>
internal interface IBackendManagerDatabase
{
    /// <summary>
    /// Logs a remote operation performed on the backend.
    /// </summary>
    /// <param name="operation">The operation performed.</param>
    /// <param name="path">The path involved.</param>
    /// <param name="data">Any data relating to the operation.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the operation log has been recorded.</returns>
    Task LogRemoteOperationAsync(string operation, string path, string? data, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the state of a remote volume in the database.
    /// </summary>
    /// <param name="name">The name of the remote volume to update.</param>
    /// <param name="state">The new state of the remote volume.</param>
    /// <param name="size">The size of the remote volume in bytes.</param>
    /// <param name="hash">The hash of the remote volume, or null if not applicable.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the remote volume has been updated.</returns>
    Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string? hash, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the state of a remote volume in the database.
    /// </summary>
    /// <param name="name">The name of the remote volume to update.</param>
    /// <param name="state">The new state of the remote volume.</param>
    /// <param name="size">The size of the remote volume in bytes.</param>
    /// <param name="hash">The hash of the remote volume, or null if not applicable.</param>
    /// <param name="suppressCleanup">If true, suppresses cleanup of the remote volume after updating.</param>
    /// <param name="deleteGraceTime">The time after which the remote volume can be deleted.</param>
    /// <param name="setArchived">If true, sets the remote volume as archived.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the remote volume has been updated.</returns>
    Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string? hash, bool suppressCleanup, TimeSpan deleteGraceTime, bool? setArchived, CancellationToken cancellationToken);

    /// <summary>
    /// Renames a remote file in the database, preserving its ID links.
    /// </summary>
    /// <param name="oldname">The current name of the remote file.</param>
    /// <param name="newname">The new name for the remote file.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the renaming operation is finished.</returns>
    Task RenameRemoteFileAsync(string oldname, string newname, CancellationToken cancellationToken);

    /// <summary>
    /// Removes multiple remote volumes from the database.
    /// </summary>
    /// <param name="names">An enumerable collection of names of remote volumes to remove.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the remote volumes have been removed.</returns>
    Task RemoveRemoteVolumesAsync(IEnumerable<string> names, CancellationToken cancellationToken);

    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the transaction has been committed.</returns>
    Task CommitAsync(CancellationToken cancellationToken);
}
