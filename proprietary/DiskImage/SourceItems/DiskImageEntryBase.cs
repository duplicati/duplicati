// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

internal abstract class DiskImageEntryBase(string path) : ISourceProviderEntry
{
    public virtual bool IsFolder => false;
    public virtual bool IsMetaEntry => false;
    public virtual bool IsRootEntry => false;
    public virtual DateTime CreatedUtc => DateTime.UnixEpoch;
    public virtual DateTime LastModificationUtc => DateTime.UnixEpoch;
    public string Path => path;
    public virtual long Size => -1;
    public bool IsSymlink => false;
    public string? SymlinkTarget => null;
    public virtual FileAttributes Attributes => IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
    public bool IsBlockDevice => false;
    public bool IsCharacterDevice => false;
    public bool IsAlternateStream => false;
    public string? HardlinkTargetId => null;

    public virtual Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This entry does not support reading.");
    }

    public abstract IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken);

    public virtual Task<bool> FileExists(string filename, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public virtual Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>());
    }
}
