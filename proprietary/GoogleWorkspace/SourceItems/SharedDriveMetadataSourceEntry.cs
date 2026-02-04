// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Drive.v3.Data;
using System.Text.Json;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class SharedDriveMetadataSourceEntry(string parentPath, Drive drive)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "metadata.json"), drive.CreatedTimeDateTimeOffset.HasValue ? drive.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(drive, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFileMetadata.ToString() }, // Reuse or new type?
            { "gsuite:Name", "metadata.json" },
            { "gsuite:id", drive.Id }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
