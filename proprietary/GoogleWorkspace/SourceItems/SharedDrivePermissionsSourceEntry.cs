// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Drive.v3.Data;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class SharedDrivePermissionsSourceEntry(SourceProvider provider, string parentPath, string userId, Drive drive)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "permissions.json"), drive.CreatedTimeDateTimeOffset.HasValue ? drive.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDriveService(userId);
        var request = service.Permissions.List(drive.Id);
        request.SupportsAllDrives = true;
        var permissions = await request.ExecuteAsync(cancellationToken);

        var json = JsonSerializer.Serialize(permissions, new JsonSerializerOptions { WriteIndented = true });
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DrivePermission.ToString() },
            { "gsuite:Name", "permissions.json" },
            { "gsuite:Id", drive.Id }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
