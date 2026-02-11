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

        if (GoogleMimeTypes.IsGoogleDoc(file.MimeType))
        {
            var exportMimeType = GoogleMimeTypes.GetExportMimeType(file.MimeType);
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

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFileContent.ToString() },
            { "gsuite:Name", "content" },
            { "gsuite:Id", file.Id },
            { "gsuite:MimeType", file.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
