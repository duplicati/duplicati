// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.SourceProvider.ChangeTracking;
using System.Text.Json;
using File = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFilePermissionsSourceEntry(SourceProvider provider, string? stateKey, string parentPath, File file, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "permissions.json"), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.ChangeTracker.OpenWithFailureTrackingAsync(stateKey, this.Path, async ct =>
        {
            var request = driveService.Permissions.List(file.Id);
            request.SupportsAllDrives = true;
            request.Fields = "permissions(id,role,type,emailAddress,domain,allowFileDiscovery)";
            var permissions = await request.ExecuteAsync(ct).ConfigureAwait(false);

            var json = JsonSerializer.Serialize(permissions, new JsonSerializerOptions { WriteIndented = true });
            return (Stream)new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        }, cancellationToken);

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DrivePermission.ToString() },
            { "gsuite:Name", "permissions.json" },
            { "gsuite:Id", file.Id }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
