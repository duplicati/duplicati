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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.DiskImage.SourceItems;

/// <summary>
/// Base class for all disk image source entries.
/// Provides common implementation for entries in the disk image hierarchy.
/// </summary>
internal abstract class DiskImageEntryBase(string path) : ISourceProviderEntry
{
    /// <inheritdoc />
    public virtual bool IsFolder => false;

    /// <inheritdoc />
    public virtual bool IsMetaEntry => false;

    /// <inheritdoc />
    public virtual bool IsRootEntry => false;

    /// <inheritdoc />
    public virtual DateTime CreatedUtc => DateTime.UnixEpoch;

    /// <inheritdoc />
    public virtual DateTime LastModificationUtc => DateTime.UnixEpoch;

    /// <inheritdoc />
    public string Path => path;

    /// <inheritdoc />
    public virtual long Size => -1;

    /// <inheritdoc />
    public bool IsSymlink => false;

    /// <inheritdoc />
    public string? SymlinkTarget => null;

    /// <inheritdoc />
    public virtual FileAttributes Attributes => IsFolder ? FileAttributes.Directory : FileAttributes.Normal;

    /// <inheritdoc />
    public bool IsSpecialFile => false;

    /// <inheritdoc />
    public bool IsCharacterDevice => false;

    /// <inheritdoc />
    public bool IsAlternateStream => false;

    /// <inheritdoc />
    public string? HardlinkTargetId => null;

    /// <inheritdoc />
    public virtual Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This entry does not support reading.");
    }

    /// <inheritdoc />
    public abstract IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken);

    /// <inheritdoc />
    public virtual Task<bool> FileExists(string filename, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public virtual Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>());
    }
}
