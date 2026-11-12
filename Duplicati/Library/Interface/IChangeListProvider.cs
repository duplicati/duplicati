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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface;

/// <summary>
/// What a change list provider hands a backup run
/// </summary>
/// <param name="ListPath">
/// The path to a change list file in the format accepted by the <c>--changed-files-list</c>
/// option, or <c>null</c> if the run must scan everything. The provider owns the file
/// until the run ends.
/// </param>
/// <param name="Token">
/// Identifies the recording that starts with this run. The backup stores it with the
/// fileset it produces, unless the run is cut short, so the next run can tell whether its
/// list was recorded against that fileset.
/// </param>
/// <param name="BaseTokens">
/// The tokens of the filesets the list is relative to. The list may only be used when the
/// previous fileset carries one of them; otherwise the run must scan everything.
/// </param>
public sealed record ChangeListHandoff(string? ListPath, string Token, string[] BaseTokens);

/// <summary>
/// Supplies a backup with the list of paths that changed since the previous backup,
/// so the backup can examine only those and copy everything else from the previous
/// fileset. The host (typically the server with a file system watcher) implements it.
/// </summary>
public interface IChangeListProvider
{
    /// <summary>
    /// Called by the backup once the source provider, and therefore the snapshot when
    /// one is used, exists and before enumeration starts. The provider should close its
    /// recording at this point and start a new one, so that the next backup receives
    /// everything that happens from here on.
    /// </summary>
    /// <param name="referenceTimeUtc">
    /// The time taken immediately before the snapshot was created. Every file the run
    /// reads reflects the file system at or after this instant, so a change observed
    /// after it may or may not be in what the run reads and should also be reported to
    /// the next run.
    /// </param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The handoff, or <c>null</c> if the provider tracks nothing for this backup</returns>
    Task<ChangeListHandoff?> DrainChangeListAsync(DateTime referenceTimeUtc, CancellationToken cancellationToken);
}
