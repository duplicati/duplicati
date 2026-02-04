// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.PeopleService.v1.Data;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ContactPhotoSourceEntry(string parentPath, Photo photo)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "photo.jpg"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(photo.Url))
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(photo.Url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var stream = new MemoryStream();
                await response.Content.CopyToAsync(stream, cancellationToken);
                stream.Seek(0, SeekOrigin.Begin);
                return stream;
            }
        }

        throw new FileNotFoundException("Cannot download photo");
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ContactPhoto.ToString() },
            { "gsuite:Name", "photo.jpg" },
            { "gsuite:id", photo.Metadata?.Source?.Id ?? photo.Url }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
