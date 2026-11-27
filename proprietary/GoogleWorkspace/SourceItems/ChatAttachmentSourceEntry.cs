// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Drive.v3;
using Google.Apis.HangoutsChat.v1.Data;
using Google.Apis.HangoutsChat.v1;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatAttachmentSourceEntry(SourceProvider provider, string parentPath, Attachment attachment, HangoutsChatService chatService, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, attachment.Name.Split('/').Last()), DateTime.UnixEpoch, DateTime.UnixEpoch)
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
            return OpenDriveFileAsync(attachment.DriveDataRef.DriveFileId, cancellationToken);

        throw new FileNotFoundException("Attachment contains no valid data reference.");
    }

    /// <summary>
    /// Opens an attachment kept in Drive. The attachment only names the file, so the type
    /// and size are asked for first: a Docs Editors file shared in a chat has no binary
    /// content and is exported, like the same file in a drive. A file the user of the
    /// backup is not handed, or that was removed since it was shared, is reported as what
    /// it is
    /// </summary>
    /// <param name="fileId">The id of the Drive file</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content</returns>
    private async Task<Stream> OpenDriveFileAsync(string fileId, CancellationToken cancellationToken)
    {
        try
        {
            var request = driveService.Files.Get(fileId);
            request.SupportsAllDrives = true;
            request.Fields = "mimeType,size";
            var file = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            return await GoogleDownloads.OpenDriveFileAsync(driveService, fileId, file.MimeType, file.Size, provider.ApiHelper.Timeouts.ReadWriteTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var refusal = GoogleServiceErrors.AsContentRefusal(ex, $"the content of attachment {fileId}");
            if (ReferenceEquals(refusal, ex))
                throw;
            throw refusal;
        }
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
