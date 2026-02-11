// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.HangoutsChat.v1.Data;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ChatAttachmentSourceEntry(string parentPath, Attachment attachment)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, attachment.ContentName ?? attachment.Name.Split('/').Last()), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(attachment.DownloadUri))
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(attachment.DownloadUri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var stream = new MemoryStream();
                await response.Content.CopyToAsync(stream, cancellationToken);
                stream.Seek(0, SeekOrigin.Begin);
                return stream;
            }
        }

        throw new FileNotFoundException("Cannot download attachment");
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
