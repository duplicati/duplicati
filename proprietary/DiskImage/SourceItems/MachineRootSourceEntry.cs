using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

public class MachineRootSourceEntry() : ISourceProviderEntry
{
    public bool IsFolder => true;

    public bool IsMetaEntry => true;

    public bool IsRootEntry => true;

    public DateTime CreatedUtc => new DateTime(0);

    public DateTime LastModificationUtc => new DateTime(0);

    public string Path => "/";

    public long Size => -1;

    public bool IsSymlink => false;

    public string? SymlinkTarget => null;

    public FileAttributes Attributes => FileAttributes.None;

    public bool IsBlockDevice => true;

    public bool IsCharacterDevice => false;

    public bool IsAlternateStream => false;

    public string? HardlinkTargetId => null;

    public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
        => SourceProvider.ListPhysicalDrives(cancellationToken);

    public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
        {
            { "diskimage:Type", "root" },
        });

    public Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
