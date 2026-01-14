// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class DriveFileSourceEntry(SourceProvider provider, string path, GraphDrive drive, GraphDriveItem item)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(path, item.Id))
{
    public override DateTime CreatedUtc => item.CreatedDateTime.FromGraphDateTime();

    public override DateTime LastModificationUtc => item.LastModifiedDateTime.FromGraphDateTime();

    public override long Size => item.Size ?? -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.OneDriveApi.GetDriveItemContentStreamAsync(drive.Id, item.Id, cancellationToken);

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", item.Id },
                { "o365:Type", SourceItemType.DriveFile.ToString() },
                { "o365:Name", item.Name ?? "" },
                { "o365:ETag", item.ETag ?? "" },
                { "o365:CTag", item.CTag ?? "" },
                { "o365:MimeType", item.File?.MimeType ?? "" },
                { "o365:ParentReference", JsonSerializer.Serialize(item.ParentReference) ?? "" },
                { "o365:FileSystemInfo", JsonSerializer.Serialize(item.FileSystemInfo) ?? "" },
                { "o365:DownloadUrl", item.DownloadUrl ?? "" },
                { "o365:Hashes", JsonSerializer.Serialize(item.File?.Hashes) ?? "" }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
