// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Drive.v3;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <param name="provider">The source provider</param>
/// <param name="parentPath">The path of the folder the drive folder is placed in</param>
/// <param name="seatRootPath">The path of the Shared Drives folder, which the seat count is kept under</param>
/// <param name="userId">The user the drive is listed through, or <c>null</c> at the tenant level</param>
/// <param name="userIsInactive">Whether the user the drive is listed through is suspended or archived</param>
/// <param name="drive">The drive</param>
/// <param name="driveService">The Drive service to read the drive with</param>
/// <param name="changeTrackingScope">The scope the change tracking state of a drive listed at the tenant level is stored under, or <c>null</c> for the default</param>
internal class SharedDriveSourceEntry(SourceProvider provider, string parentPath, string seatRootPath, string? userId, bool userIsInactive, Drive drive, DriveService driveService, string? changeTrackingScope)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, drive.Id)), drive.CreatedTimeDateTimeOffset.HasValue ? drive.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!provider.LicenseApprovedForEntry(seatRootPath, GoogleRootType.SharedDrives, drive.Id, increment: true, countsAsSeat: true))
            yield break;

        // The drive tree below reads the scope when it builds its change tracking key
        if (userId == null)
            provider.SetChangeTrackingSharedDriveScope(drive.Id, changeTrackingScope);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new SharedDriveMetadataSourceEntry(this.Path, drive);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new SharedDrivePermissionsSourceEntry(this.Path, drive, driveService);
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveTreeSourceEntry(provider, this.Path, userId, userIsInactive, "Content", drive.Id, drive.Id, driveService);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.SharedDrives.ToString() }, // Or specific type?
            { "gsuite:Name", drive.Name },
            { "gsuite:Id", drive.Id },
            { "gsuite:Kind", drive.Kind },
            { "gsuite:OrgUnitId", drive.OrgUnitId }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
