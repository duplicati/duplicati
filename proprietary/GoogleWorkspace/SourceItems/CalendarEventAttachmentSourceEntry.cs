// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.SourceProvider.ChangeTracking;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class CalendarEventAttachmentSourceEntry(SourceProvider provider, string? stateKey, string parentPath, EventAttachment attachment, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, attachment.Title), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.ChangeTracker.OpenWithFailureTrackingAsync(stateKey, this.Path, ct => DownloadAsync(ct), cancellationToken);

    /// <summary>
    /// Downloads the attachment
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content</returns>
    private async Task<Stream> DownloadAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(attachment.FileId))
        {
            var request = driveService.Files.Get(attachment.FileId);
            request.SupportsAllDrives = true;
            return await GoogleDownloads.ToMemoryAsync(request.DownloadAsync, $"attachment {attachment.FileId}", cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrEmpty(attachment.FileUrl))
        {
            using var client = new HttpClient();
            return await GoogleDownloads.ToMemoryAsync(client, attachment.FileUrl, $"attachment {attachment.FileUrl}", cancellationToken).ConfigureAwait(false);
        }

        throw new FileNotFoundException("Cannot download attachment");
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.CalendarEventAttachment.ToString() },
            { "gsuite:Name", attachment.Title },
            { "gsuite:Id", attachment.FileId ?? attachment.FileUrl },
            { "gsuite:MimeType", attachment.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
