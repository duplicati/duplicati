// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Drive.v3;
using Google.Apis.HangoutsChat.v1.Data;
using Google.Apis.HangoutsChat.v1;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatAttachmentSourceEntry(SourceProvider provider, string parentPath, Attachment attachment, HangoutsChatService chatService, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, attachment.ContentName ?? attachment.Name.Split('/').Last()), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var readTimeout = provider.ApiHelper.Timeouts.ReadWriteTimeout;

        // An attachment uploaded to Chat is served by the Chat media endpoint
        if (attachment.AttachmentDataRef != null)
        {
            var request = chatService.Media.Download(attachment.AttachmentDataRef.ResourceName);
            request.Alt = MediaResource.DownloadRequest.AltEnum.Media;
            return GoogleDownloads.OpenMediaAsync(request, null, readTimeout, $"attachment {attachment.AttachmentDataRef.ResourceName}", cancellationToken);
        }

        // An attachment stored in Drive is read through the Drive client
        if (attachment.DriveDataRef != null)
        {
            var request = driveService.Files.Get(attachment.DriveDataRef.DriveFileId);
            request.SupportsAllDrives = true;
            request.Alt = FilesResource.GetRequest.AltEnum.Media;
            return GoogleDownloads.OpenMediaAsync(request, null, readTimeout, $"attachment {attachment.DriveDataRef.DriveFileId}", cancellationToken);
        }

        throw new FileNotFoundException("Attachment contains no valid data reference.");
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ChatAttachment.ToString() },
            { "gsuite:Name", attachment.ContentName ?? attachment.Name.Split('/').Last() },
            { "gsuite:Id", attachment.Name },
            { "gsuite:ContentType", attachment.ContentType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
