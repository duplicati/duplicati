// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.SourceProvider.ChangeTracking;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class CalendarEventAttachmentSourceEntry(SourceProvider provider, string? stateKey, string parentPath, EventAttachment attachment, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, EntryName(attachment)), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    /// <summary>
    /// The name of the entry of an attachment: the id of the Drive file it refers to.
    /// The path uses the id, as a title is neither unique nor safe to use in a path.
    /// An attachment that is only a link has no id, so its name is made from the link.
    /// </summary>
    /// <param name="attachment">The attachment</param>
    /// <returns>The entry name</returns>
    internal static string EntryName(EventAttachment attachment)
    {
        if (!string.IsNullOrEmpty(attachment.FileId))
            return attachment.FileId;

        var source = attachment.FileUrl ?? attachment.Title ?? string.Empty;
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source));
        return "link-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

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
        var readTimeout = provider.ApiHelper.Timeouts.ReadWriteTimeout;
        if (!string.IsNullOrEmpty(attachment.FileId))
        {
            // The attachment is a Drive file, read through the Drive client with its credentials
            var request = driveService.Files.Get(attachment.FileId);
            request.SupportsAllDrives = true;
            request.Alt = FilesResource.GetRequest.AltEnum.Media;
            return await GoogleDownloads.OpenMediaAsync(request, null, readTimeout, $"attachment {attachment.FileId}", cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrEmpty(attachment.FileUrl))
        {
            // An attachment that is only a link is fetched as is, without credentials: the link
            // may point anywhere, and a Google token must not be sent to an unknown host
            return await GoogleDownloads.OpenMediaAsync(provider.LinkDownloadClient, attachment.FileUrl, null, readTimeout, $"attachment {attachment.FileUrl}", cancellationToken).ConfigureAwait(false);
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
