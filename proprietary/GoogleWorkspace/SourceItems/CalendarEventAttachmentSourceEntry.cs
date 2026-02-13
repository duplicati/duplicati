// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Calendar.v3.Data;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class CalendarEventAttachmentSourceEntry(SourceProvider provider, string parentPath, string userId, EventAttachment attachment)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, attachment.Title), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(attachment.FileId))
        {
            var service = provider.ApiHelper.GetDriveService(userId);
            var request = service.Files.Get(attachment.FileId);
            var stream = new MemoryStream();
            await request.DownloadAsync(stream, cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }
        else if (!string.IsNullOrEmpty(attachment.FileUrl))
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(attachment.FileUrl, cancellationToken);
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
            { "gsuite:Type", SourceItemType.CalendarEventAttachment.ToString() },
            { "gsuite:Name", attachment.Title },
            { "gsuite:Id", attachment.FileId ?? attachment.FileUrl },
            { "gsuite:MimeType", attachment.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
