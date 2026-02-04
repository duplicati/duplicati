// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using System.Text.Json;
using File = Google.Apis.Drive.v3.Data.File;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFilePermissionsSourceEntry(SourceProvider provider, string parentPath, File file)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "permissions.json"), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDriveService();
        var request = service.Permissions.List(file.Id);
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
            { "gsuite:id", file.Id }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
