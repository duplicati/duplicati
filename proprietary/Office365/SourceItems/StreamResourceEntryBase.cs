// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal abstract class StreamResourceEntryBase(string path) : ISourceProviderEntry
{
    public bool IsFolder => false;

    public bool IsMetaEntry => false;

    public virtual bool IsRootEntry => false;

    public abstract DateTime CreatedUtc { get; }

    public abstract DateTime LastModificationUtc { get; }

    public string Path => path;

    public abstract long Size { get; }
    public bool IsSymlink => false;

    public string? SymlinkTarget => null;

    public virtual FileAttributes Attributes => FileAttributes.Normal;

    public bool IsBlockDevice => false;

    public bool IsCharacterDevice => false;

    public bool IsAlternateStream => false;
    public string? HardlinkTargetId => null;

    public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
        => throw new NotSupportedException("Cannot enumerate a file entry");

    public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => throw new NotSupportedException("Cannot enumerate a file entry");

    public virtual Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>());

    public abstract Task<Stream> OpenRead(CancellationToken cancellationToken);
}