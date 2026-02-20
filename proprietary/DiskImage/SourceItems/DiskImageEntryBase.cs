// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

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
    public bool IsBlockDevice => false;

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
