// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal abstract class MetaEntryBase(string path, DateTime? createdUtc, DateTime? lastModificationUtc) : ISourceProviderEntry
{
    public bool IsFolder => true;

    public virtual bool IsMetaEntry => false;

    public bool IsRootEntry => false;

    public DateTime CreatedUtc => createdUtc ?? DateTime.UnixEpoch;

    public DateTime LastModificationUtc => lastModificationUtc ?? DateTime.UnixEpoch;

    public string Path => path;

    public long Size => -1;
    public bool IsSymlink => false;

    public string? SymlinkTarget => null;

    public FileAttributes Attributes => FileAttributes.Directory;

    public bool IsBlockDevice => false;

    public bool IsCharacterDevice => false;

    public bool IsAlternateStream => false;
    public string? HardlinkTargetId => null;

    public abstract IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken);

    public virtual Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public virtual Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>());

    public Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
