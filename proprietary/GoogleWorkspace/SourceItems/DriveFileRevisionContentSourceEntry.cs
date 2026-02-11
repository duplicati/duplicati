// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Google.Apis.Drive.v3.Data;
using File = Google.Apis.Drive.v3.Data.File;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFileRevisionContentSourceEntry(SourceProvider provider, string parentPath, File file, Revision revision)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, revision.Id), revision.ModifiedTimeDateTimeOffset.HasValue ? revision.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, revision.ModifiedTimeDateTimeOffset.HasValue ? revision.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => revision.Size ?? -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDriveService();
        var url = $"https://www.googleapis.com/drive/v3/files/{file.Id}/revisions/{revision.Id}?alt=media";

        if (GoogleMimeTypes.IsGoogleDoc(file.MimeType))
        {
            var exportMimeType = GoogleMimeTypes.GetExportMimeType(file.MimeType);
            if (revision.ExportLinks != null && revision.ExportLinks.ContainsKey(exportMimeType))
            {
                url = revision.ExportLinks[exportMimeType];
            }
            else
            {
                // Try to construct export URL if not present?
                // Or skip.
                // For now, let's try the constructed URL, but it might fail for Docs.
                // Actually, for Docs, we can't download via alt=media.
                // If ExportLinks is missing, we might be out of luck for that revision.
                if (revision.ExportLinks == null || !revision.ExportLinks.ContainsKey(exportMimeType))
                {
                    throw new NotSupportedException($"Cannot export revision {revision.Id} for mimeType {file.MimeType}");
                }
            }
        }

        var response = await service.HttpClient.GetAsync(url, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var stream = new MemoryStream();
            await response.Content.CopyToAsync(stream, cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

        throw new FileNotFoundException($"Cannot download revision: {response.StatusCode}");
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFileRevision.ToString() },
            { "gsuite:Name", revision.Id },
            { "gsuite:Id", revision.Id },
            { "gsuite:MimeType", revision.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
