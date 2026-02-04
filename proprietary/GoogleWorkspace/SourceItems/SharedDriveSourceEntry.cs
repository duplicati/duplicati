// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Drive.v3.Data;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class SharedDriveSourceEntry(SourceProvider provider, string userId, string parentPath, Drive drive)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, drive.Name)), drive.CreatedTimeDateTimeOffset.HasValue ? drive.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new SharedDriveMetadataSourceEntry(this.Path, drive);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new SharedDrivePermissionsSourceEntry(provider, this.Path, drive);
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveFolderSourceEntry(provider, userId, this.Path, "Content", drive.Id);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.SharedDrives.ToString() }, // Or specific type?
            { "gsuite:Name", drive.Name },
            { "gsuite:id", drive.Id }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
