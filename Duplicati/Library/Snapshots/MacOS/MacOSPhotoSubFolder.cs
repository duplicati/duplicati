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
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Snapshots.MacOS;

/// <summary>
/// Implements a subfolder within a MacOS Photos library export structure
/// </summary>
/// <param name="parent">The library parent</param>
/// <param name="subpath">The subfolder path</param>
/// <param name="children">The child entries within the subfolder</param>
[SupportedOSPlatform("macOS")]
internal class MacOSPhotoSubFolder(MacOSPhotosLibraryEntry parent, string subpath, IReadOnlyList<ISourceProviderEntry> children) : ISourceProviderEntry
{
    /// <inheritdoc/>
    public bool IsFolder => true;

    /// <inheritdoc/>
    public bool IsMetaEntry => false;

    /// <inheritdoc/>
    public bool IsRootEntry => false;

    /// <inheritdoc/>
    public DateTime CreatedUtc => parent.CreatedUtc;

    /// <inheritdoc/>
    public DateTime LastModificationUtc => parent.LastModificationUtc;

    /// <inheritdoc/>
    public string Path => Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parent.Path, subpath));

    /// <inheritdoc/>
    public long Size => -1;

    /// <inheritdoc/>
    public bool IsSymlink => false;

    /// <inheritdoc/>
    public string? SymlinkTarget => null;

    /// <inheritdoc/>
    public FileAttributes Attributes => parent.Attributes;

    /// <inheritdoc/>
    public Dictionary<string, string> MinorMetadata => new Dictionary<string, string>();

    /// <inheritdoc/>
    public bool IsBlockDevice => false;

    /// <inheritdoc/>
    public bool IsCharacterDevice => false;

    /// <inheritdoc/>
    public bool IsAlternateStream => false;

    /// <inheritdoc/>
    public string? HardlinkTargetId => null;

    /// <inheritdoc/>
    public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
        => children.ToAsyncEnumerable();

    /// <inheritdoc/>
    public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
    {
        var fullpath = System.IO.Path.Combine(Path, filename);
        return Task.FromResult(children.Any(e => e.Path.Equals(fullpath, Utility.Utility.ClientFilenameStringComparison)));
    }

    /// <inheritdoc/>
    public Task<Stream?> OpenMetadataRead(CancellationToken cancellationToken)
        => Task.FromResult<Stream?>(null);

    /// <inheritdoc/>
    public Task<Stream> OpenRead(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Cannot open a folder for reading");
}
