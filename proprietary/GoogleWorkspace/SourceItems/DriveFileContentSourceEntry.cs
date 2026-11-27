// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.SourceProvider.ChangeTracking;
using File = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Drive.v3;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFileContentSourceEntry(SourceProvider provider, string? stateKey, string parentPath, File file, DriveService driveService)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(parentPath, "content"), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
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
        => GoogleDownloads.OpenDriveFileAsync(driveService, file.Id, file.MimeType, file.Size, provider.ApiHelper.Timeouts.ReadWriteTimeout, cancellationToken);

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
