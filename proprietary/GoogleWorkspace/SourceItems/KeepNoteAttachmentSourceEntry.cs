// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Keep.v1.Data;
using Google.Apis.Keep.v1;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class KeepNoteAttachmentSourceEntry(SourceProvider provider, string parentPath, Attachment attachment, KeepService keepService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, attachment.Name.Split('/').Last()), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        // attachment.Name is like "notes/123/attachments/456"; the media endpoint serves its
        // bytes, read through the Keep client with its credentials
        var request = keepService.Media.Download(attachment.Name);
        request.Alt = MediaResource.DownloadRequest.AltEnum.Media;
        return GoogleDownloads.OpenMediaAsync(request, null, provider.ApiHelper.Timeouts.ReadWriteTimeout, $"attachment {attachment.Name}", cancellationToken);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.KeepNoteAttachment.ToString() },
            { "gsuite:Name", attachment.Name.Split('/').Last() },
            { "gsuite:Id", attachment.Name },
            { "gsuite:MimeType", attachment.MimeType != null ? string.Join(",", attachment.MimeType) : null }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
