// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Keep.v1.Data;
using Google.Apis.Keep.v1;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class KeepNoteAttachmentSourceEntry(string parentPath, Attachment attachment, KeepService keepService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, attachment.Name.Split('/').Last()), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        // Keep API attachments are downloaded via media URL?
        // attachment.Name is like "notes/123/attachments/456".
        // Docs say: GET https://keep.googleapis.com/v1/notes/{noteId}/attachments/{attachmentId}?alt=media

        var url = $"https://keep.googleapis.com/v1/{attachment.Name}?alt=media";

        var response = await keepService.HttpClient.GetAsync(url, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var stream = new MemoryStream();
            await response.Content.CopyToAsync(stream, cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

        throw new FileNotFoundException($"Cannot download attachment: {response.StatusCode}");
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
