// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using File = Google.Apis.Drive.v3.Data.File;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFileContentSourceEntry(SourceProvider provider, string parentPath, File file)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "content"), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override long Size => file.Size ?? -1;

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDriveService();

        if (IsGoogleDoc(file.MimeType))
        {
            var exportMimeType = GetExportMimeType(file.MimeType);
            var request = service.Files.Export(file.Id, exportMimeType);
            var stream = new MemoryStream();
            await request.DownloadAsync(stream, cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }
        else
        {
            var request = service.Files.Get(file.Id);
            var stream = new MemoryStream();
            await request.DownloadAsync(stream, cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }
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
            { "gsuite:Type", SourceItemType.DriveFileContent.ToString() },
            { "gsuite:Name", "content" },
            { "gsuite:id", file.Id },
            { "gsuite:MimeType", file.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
