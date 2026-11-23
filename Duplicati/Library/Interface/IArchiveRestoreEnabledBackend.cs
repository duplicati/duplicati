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

#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface;

/// <summary>
/// Whether a remote file can be read now
/// </summary>
public enum ArchiveRestoreState
{
    /// <summary>
    /// The file can be read
    /// </summary>
    Available,
    /// <summary>
    /// The file is in archive storage and has to be brought back before it can be read
    /// </summary>
    Archived,
    /// <summary>
    /// The file is being brought back from archive storage
    /// </summary>
    RestoreInProgress
}

/// <summary>
/// A backend that can bring files back from archive storage, such as Amazon S3 Glacier or the
/// Azure archive tier, where they cannot be read until they are brought back. That can take
/// hours, so the request is made ahead of the restore that reads the files.
/// </summary>
public interface IArchiveRestoreEnabledBackend : IBackend
{
    /// <summary>
    /// Gets whether the remote file can be read now
    /// </summary>
    /// <param name="remotename">The name of the remote file</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The state of the remote file</returns>
    Task<ArchiveRestoreState> GetArchiveRestoreStateAsync(string remotename, CancellationToken cancellationToken);

    /// <summary>
    /// Asks for the remote file to be brought back from archive storage. Asking again while the
    /// file is being brought back is not an error.
    /// </summary>
    /// <param name="remotename">The name of the remote file</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task RequestArchiveRestoreAsync(string remotename, CancellationToken cancellationToken);
}
