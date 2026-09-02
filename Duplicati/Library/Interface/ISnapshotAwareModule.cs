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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface;

/// <summary>
/// Interface for source provider modules that can participate in snapshot operations.
/// When a snapshot is created for the backup (e.g., VSS on Windows), modules implementing
/// this interface will be asked to contribute paths to the snapshot and will receive
/// the snapshot service for use during backup operations.
/// </summary>
public interface ISnapshotAwareModule
{
    /// <summary>
    /// Gets the paths that should be included in the snapshot.
    /// This is called before the snapshot is created, so the paths can be included
    /// in the snapshot set.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The paths to include in the snapshot, or an empty collection if none.</returns>
    Task<IEnumerable<string>> GetSnapshotPathsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the snapshot service for the module to use.
    /// This is called after the snapshot has been created (or after snapshot creation
    /// has been skipped/failed). The module should use the snapshot service for
    /// subsequent operations.
    /// </summary>
    /// <param name="snapshotService">The snapshot service, or null if no snapshot was created.</param>
    void SetSnapshotService(ISnapshotService? snapshotService);
}
