// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.SourceProvider.ChangeTracking;
using Google.Apis.Drive.v3.Data;
using File = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFileRevisionContentSourceEntry(SourceProvider provider, string? stateKey, string parentPath, File file, Revision revision, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, revision.Id), revision.ModifiedTimeDateTimeOffset.HasValue ? revision.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, revision.ModifiedTimeDateTimeOffset.HasValue ? revision.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => revision.Size ?? -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.ChangeTracker.OpenWithFailureTrackingAsync(stateKey, this.Path, ct => DownloadAsync(ct), cancellationToken);

    /// <summary>
    /// Downloads the revision
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content</returns>
    private Task<Stream> DownloadAsync(CancellationToken cancellationToken)
    {
        if (GoogleMimeTypes.IsGoogleDoc(file.MimeType))
        {
            // A Docs Editors revision has no binary content; it is exported through the link
            // Drive hands out for it, and an export has no length anyone can state
            var exportMimeType = GoogleMimeTypes.GetExportMimeType(file.MimeType);
            if (revision.ExportLinks == null || !revision.ExportLinks.TryGetValue(exportMimeType, out var exportUrl))
                throw new NotSupportedException($"Cannot export revision {revision.Id} for mimeType {file.MimeType}");

            return GoogleDownloads.OpenMediaAsync(driveService.HttpClient, exportUrl, null, provider.ApiHelper.Timeouts.ReadWriteTimeout, $"revision {revision.Id}", cancellationToken);
        }

        // Binary content is read straight from the connection, whatever its size
        var request = driveService.Revisions.Get(file.Id, revision.Id);
        request.Alt = RevisionsResource.GetRequest.AltEnum.Media;
        return GoogleDownloads.OpenMediaAsync(request, revision.Size, provider.ApiHelper.Timeouts.ReadWriteTimeout, $"revision {revision.Id}", cancellationToken);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFileRevision.ToString() },
            { "gsuite:Name", revision.Id },
            { "gsuite:Id", revision.Id },
            { "gsuite:MimeType", revision.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
