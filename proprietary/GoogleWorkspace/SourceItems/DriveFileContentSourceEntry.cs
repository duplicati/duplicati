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
                // A Docs Editors file has no binary content; it is exported. The size Drive lists
                // for it is its storage footprint, not the size of the export, so no length is given
                var request = driveService.Files.Export(file.Id, GoogleMimeTypes.GetExportMimeType(file.MimeType));
                return GoogleDownloads.OpenMediaAsync(request, null, provider.ApiHelper.Timeouts.ReadWriteTimeout, $"file {file.Id}", ct);
            }
            else
            {
                // Binary content is read straight from the connection, whatever its size; the
                // service's client carries the credentials and the retries of the request
                var request = driveService.Files.Get(file.Id);
                request.SupportsAllDrives = true;
                request.Alt = FilesResource.GetRequest.AltEnum.Media;
                return GoogleDownloads.OpenMediaAsync(request, file.Size, provider.ApiHelper.Timeouts.ReadWriteTimeout, $"file {file.Id}", ct);
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
