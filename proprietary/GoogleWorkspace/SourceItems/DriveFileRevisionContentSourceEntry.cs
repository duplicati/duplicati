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

        if (IsGoogleDoc(file.MimeType))
        {
            var exportMimeType = GetExportMimeType(file.MimeType);
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

    private static bool IsGoogleDoc(string mimeType)
    {
        return mimeType.StartsWith("application/vnd.google-apps.") &&
               mimeType != "application/vnd.google-apps.folder" &&
               mimeType != "application/vnd.google-apps.shortcut";
    }

    private static string GetExportMimeType(string mimeType)
    {
        return mimeType switch
        {
            "application/vnd.google-apps.document" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.google-apps.spreadsheet" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.google-apps.presentation" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/vnd.google-apps.script" => "application/vnd.google-apps.script+json",
            _ => "application/pdf"
        };
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFileRevision.ToString() },
            { "gsuite:Name", revision.Id },
            { "gsuite:id", revision.Id },
            { "gsuite:MimeType", revision.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
