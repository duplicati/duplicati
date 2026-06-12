using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.General;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

public class PhysicalDriveSourceEntry(PhysicalDriveInfo driveInfo) : ISourceProviderEntry
{
    public bool IsFolder => true;

    public bool IsMetaEntry => true;

    public bool IsRootEntry => false;

    public DateTime CreatedUtc => new DateTime(0);

    public DateTime LastModificationUtc => new DateTime(0);

    public string Path => Util.AppendDirSeparator(driveInfo.Path);

    public long Size => (long)driveInfo.Size;

    public bool IsSymlink => false;

    public string? SymlinkTarget => null;

    public FileAttributes Attributes => FileAttributes.None;

    public bool IsBlockDevice => true;

    public bool IsCharacterDevice => false;

    public bool IsAlternateStream => false;

    public string? HardlinkTargetId => null;

    public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
        {
            { "diskimage:Type", "physicaldrive" },
            { "diskimage:Number", driveInfo.Number },
            { "diskimage:FriendlyName", driveInfo.DisplayName },
            { "diskimage:Size", driveInfo.Size.ToString() },
            { "diskimage:DevicePath", driveInfo.Path },
            { "diskimage:Name", $"{driveInfo.DisplayName} ({Library.Utility.Utility.FormatSizeString(driveInfo.Size)})" },
        });

    public Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
