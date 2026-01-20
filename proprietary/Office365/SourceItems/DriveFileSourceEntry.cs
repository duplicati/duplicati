// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class DriveFileSourceEntry(SourceProvider provider, string path, GraphDrive drive, GraphDriveItem item)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(path, item.Id))
{
    public override DateTime CreatedUtc => item.CreatedDateTime.FromGraphDateTime();

    public override DateTime LastModificationUtc => item.LastModifiedDateTime.FromGraphDateTime();

    public override long Size => item.Size ?? -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.OneDriveApi.GetDriveItemContentStreamAsync(drive.Id, item.Id, cancellationToken);

    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string?>()
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
            };

        try
        {
            var permissions = new List<GraphPermission>();
            await foreach (var perm in provider.OneDriveApi.GetDriveItemPermissionsAsync(drive.Id, item.Id, cancellationToken))
            {
                permissions.Add(perm);
            }

            if (permissions.Count > 0)
            {
                metadata["o365:Permissions"] = JsonSerializer.Serialize(permissions);
            }
        }
        catch (Exception ex)
        {
            // Log warning but don't fail the backup if permissions cannot be read
            Duplicati.Library.Logging.Log.WriteWarningMessage(Log.LogTagFromType<DriveFileSourceEntry>(), "PermissionReadError", ex, $"Failed to read permissions for file {item.Id}");
        }

        return metadata
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
