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
/// An entry restored from below the virtual mount point of a prefix-based source provider
/// </summary>
/// <param name="Path">The path the entry is stored under in the backup</param>
/// <param name="TargetPath">The path the entry was restored to</param>
/// <param name="OriginalPath">The local path the entry was backed up from, or <c>null</c> for a virtual folder</param>
/// <param name="IsFolder">True if the entry is a folder</param>
public sealed record RestoredVirtualEntry(string Path, string TargetPath, string? OriginalPath, bool IsFolder);

/// <summary>
/// A prefix-based source provider that can register the items it backed up
/// (e.g. Hyper-V virtual machines) with the application they belong to,
/// after their files have been restored
/// </summary>
public interface IRestoredItemRegistrationModule : IPrefixedSourceProviderModule
{
    /// <summary>
    /// Registers the items whose files were restored. An item is only registered when all
    /// of its files were restored, and a failure to register one item is reported as a warning
    /// without stopping the others, as the restored files are in place either way.
    /// </summary>
    /// <param name="restoredEntries">The entries restored from below the provider's mount point</param>
    /// <param name="versionPaths">The stored paths of all entries below the provider's mount point in the restored version, except the virtual folders</param>
    /// <param name="options">The options of the restore</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task RegisterRestoredItemsAsync(IReadOnlyList<RestoredVirtualEntry> restoredEntries, IReadOnlyCollection<string> versionPaths, IReadOnlyDictionary<string, string?> options, CancellationToken cancellationToken);
}
