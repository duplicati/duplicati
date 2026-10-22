// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.SourceProvider.ChangeTracking;
using File = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFileContentSourceEntry(SourceProvider provider, string? stateKey, string parentPath, File file, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "content"), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => file.Size ?? -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.ChangeTracker.OpenWithFailureTrackingAsync(stateKey, this.Path, ct =>
        {
            if (GoogleMimeTypes.IsGoogleDoc(file.MimeType))
            {
                var exportMimeType = GoogleMimeTypes.GetExportMimeType(file.MimeType);
                var request = driveService.Files.Export(file.Id, exportMimeType);
                return GoogleDownloads.ToMemoryAsync(request.DownloadAsync, $"file {file.Id}", ct);
            }
            else
            {
                var request = driveService.Files.Get(file.Id);
                request.SupportsAllDrives = true;
                return GoogleDownloads.ToMemoryAsync(request.DownloadAsync, $"file {file.Id}", ct);
            }
        }, cancellationToken);

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFileContent.ToString() },
            { "gsuite:Name", "content" },
            { "gsuite:Id", file.Id },
            { "gsuite:MimeType", file.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
