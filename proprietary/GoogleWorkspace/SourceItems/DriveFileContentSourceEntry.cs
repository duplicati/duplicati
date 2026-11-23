// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Library.SourceProvider.ChangeTracking;
using File = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFileContentSourceEntry(SourceProvider provider, string? stateKey, string parentPath, File file, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "content"), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<DriveFileContentSourceEntry>();

    public override long Size => file.Size ?? -1;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.ChangeTracker.OpenWithFailureTrackingAsync(stateKey, this.Path, DownloadAsync, cancellationToken);

    /// <summary>
    /// Downloads the content. A file whose owner turned off download for viewers, or that
    /// Drive flagged, is listed but its content is refused; that is reported as what it is
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content</returns>
    private async Task<Stream> DownloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var refusal = GoogleServiceErrors.AsContentRefusal(ex, $"the content of file {file.Id}");
            if (ReferenceEquals(refusal, ex))
                throw;
            throw refusal;
        }
    }

    /// <summary>
    /// Opens the content of the file
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content</returns>
    private Task<Stream> OpenAsync(CancellationToken cancellationToken)
    {
        // A Docs Editors file has no binary content; it is exported
        if (GoogleMimeTypes.IsGoogleDoc(file.MimeType))
            return ExportAsync(cancellationToken);

        // Binary content is read straight from the connection, whatever its size; the
        // service's client carries the credentials and the retries of the request
        var request = driveService.Files.Get(file.Id);
        request.SupportsAllDrives = true;
        request.Alt = FilesResource.GetRequest.AltEnum.Media;
        return GoogleDownloads.OpenMediaAsync(request, file.Size, provider.ApiHelper.Timeouts.ReadWriteTimeout, $"file {file.Id}", cancellationToken);
    }

    /// <summary>
    /// Exports a Docs Editors file. <c>Files.Export</c> refuses an export larger than 10 MB,
    /// so a file it refuses for its size is exported through the export link Drive hands out
    /// for the file, which has no such limit. The size Drive lists for the file is its
    /// storage footprint, not the size of the export, so no length is given
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The exported content</returns>
    private async Task<Stream> ExportAsync(CancellationToken cancellationToken)
    {
        var exportMimeType = GoogleMimeTypes.GetExportMimeType(file.MimeType);
        var request = driveService.Files.Export(file.Id, exportMimeType);
        try
        {
            return await GoogleDownloads.OpenMediaAsync(request, null, provider.ApiHelper.Timeouts.ReadWriteTimeout, $"file {file.Id}", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (GoogleServiceErrors.IsExportSizeLimitExceeded(ex))
        {
            var exportUrl = await GetExportLinkAsync(exportMimeType, cancellationToken).ConfigureAwait(false);
            if (exportUrl == null)
                throw;

            Log.WriteVerboseMessage(LOGTAG, "ExportThroughLink", null, Strings.ExportThroughLink(file.Id));
            return await GoogleDownloads.OpenMediaAsync(driveService, exportUrl, null, provider.ApiHelper.Timeouts.ReadWriteTimeout, $"file {file.Id}", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the link Drive hands out for exporting the file to the given type. The links
    /// are only asked for when needed, as the listing would otherwise carry a handful of
    /// them for every Docs Editors file
    /// </summary>
    /// <param name="exportMimeType">The type to export to</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The link, or <c>null</c> if Drive has none for the type</returns>
    private async Task<string?> GetExportLinkAsync(string exportMimeType, CancellationToken cancellationToken)
    {
        var request = driveService.Files.Get(file.Id);
        request.SupportsAllDrives = true;
        request.Fields = "exportLinks";
        var links = (await request.ExecuteAsync(cancellationToken).ConfigureAwait(false)).ExportLinks;
        return links != null && links.TryGetValue(exportMimeType, out var url) && !string.IsNullOrWhiteSpace(url) ? url : null;
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
