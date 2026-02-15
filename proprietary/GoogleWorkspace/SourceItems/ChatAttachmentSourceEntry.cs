// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Drive.v3;
using Google.Apis.HangoutsChat.v1.Data;
using Google.Apis.HangoutsChat.v1;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatAttachmentSourceEntry(string parentPath, Attachment attachment, HangoutsChatService chatService, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, attachment.ContentName ?? attachment.Name.Split('/').Last()), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        // Check if the attachment is stored in Google Chat (not Drive)
        if (attachment.AttachmentDataRef != null)
        {
            var request = chatService.Media.Download(attachment.AttachmentDataRef.ResourceName);
            return await request.ExecuteAsStreamAsync(cancellationToken);
        }

        // Check if the attachment is stored in Google Drive
        if (attachment.DriveDataRef != null)
        {
            var driveRequest = driveService.Files.Get(attachment.DriveDataRef.DriveFileId);
            driveRequest.Alt = FilesResource.GetRequest.AltEnum.Media;
            return await driveRequest.ExecuteAsStreamAsync(cancellationToken);
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
