// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.SourceProvider.ChangeTracking;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ContactPhotoSourceEntry(SourceProvider provider, string? stateKey, string parentPath, Photo photo, int index, PeopleServiceService peopleService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, $"photo-{index}.jpg"), DateTime.UnixEpoch, DateTime.UnixEpoch)
{
    public override long Size => -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.ChangeTracker.OpenWithFailureTrackingAsync(stateKey, this.Path, ct =>
        {
            if (string.IsNullOrEmpty(photo.Url))
                throw new FileNotFoundException("Cannot download photo");

            // The photo URLs are not public, so the download goes through the People service's
            // client, whose credential interceptor attaches the impersonated user's token.
            return GoogleDownloads.ToMemoryAsync(peopleService.HttpClient, photo.Url, "photo", ct);
        }, cancellationToken);

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ContactPhoto.ToString() },
            { "gsuite:Name", $"{photo.Metadata?.Source?.Type ?? ""}-photo-{index}.jpg".TrimStart('-') },
            { "gsuite:Id", photo.Metadata?.Source?.Id ?? photo.Url },
            { "gsuite:Url", photo.Url },
            { "gsuite:Default", photo.Default__?.ToString() },
            { "gsuite:MetadataSourceId", photo.Metadata?.Source?.Id },
            { "gsuite:MetadataSourceType", photo.Metadata?.Source?.Type }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
