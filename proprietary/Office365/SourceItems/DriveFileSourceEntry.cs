// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class DriveFileSourceEntry(SourceProvider provider, string path, GraphDrive drive, GraphDriveItem item)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(path, item.Id))
{
    private static readonly string LOGTAG = Log.LogTagFromType<DriveFileSourceEntry>();

    /// <summary>
    /// The hashes Graph reports for content of zero length: QuickXorHash as returned by
    /// SharePoint, and SHA-1 and SHA-256 as returned by OneDrive.
    /// </summary>
    private const string EMPTY_QUICK_XOR_HASH = "AAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string EMPTY_SHA1_HASH = "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709";
    private const string EMPTY_SHA256_HASH = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

    public override DateTime CreatedUtc => item.CreatedDateTime.FromGraphDateTime();

    public override DateTime LastModificationUtc => item.LastModifiedDateTime.FromGraphDateTime();

    public override long Size => item.Size ?? -1;

    /// <summary>
    /// Whether an item's own metadata says it has no content: a size of zero and no
    /// hash that contradicts it. SharePoint does not compute hashes for every file, so
    /// an absent hash is accepted, but a hash of non-empty content is not.
    /// </summary>
    /// <param name="candidate">The item metadata to inspect.</param>
    /// <returns><c>true</c> if the metadata describes empty content.</returns>
    private static bool DescribesEmptyContent(GraphDriveItem candidate)
    {
        if (candidate.Size != 0)
            return false;

        var hashes = candidate.File?.Hashes;
        if (hashes == null)
            return true;

        if (!string.IsNullOrEmpty(hashes.QuickXorHash) && !string.Equals(hashes.QuickXorHash, EMPTY_QUICK_XOR_HASH, StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrEmpty(hashes.Sha1Hash) && !string.Equals(hashes.Sha1Hash, EMPTY_SHA1_HASH, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(hashes.Sha256Hash) && !string.Equals(hashes.Sha256Hash, EMPTY_SHA256_HASH, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        HttpRequestException notFound;
        try
        {
            return await provider.OneDriveApi.GetDriveItemContentStreamAsync(drive.Id, item.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            notFound = ex;
        }

        // SharePoint refuses to serve some of its own internals, such as certain web part
        // definitions in the Web Part Gallery, although they appear as files in the listing.
        // The children listing can report a size for them that the item itself does not
        // have, so ask for the item directly: if that says there is no content, store it
        // as empty rather than failing the file.
        GraphDriveItem? fresh = null;
        try
        {
            fresh = await provider.OneDriveApi.GetDriveItemAsync(drive.Id, item.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.WriteVerboseMessage(LOGTAG, "ItemLookupAfterNotFoundFailed", ex, "Could not look up item {0} ('{1}') after its download was refused", item.Id, item.Name);
        }

        if (fresh != null && DescribesEmptyContent(fresh))
        {
            Log.WriteVerboseMessage(LOGTAG, "EmptyItemNotDownloadable", "Storing item {0} ('{1}') as empty because the download was refused and the item reports no content (listing said {2} bytes): {3}", item.Id, item.Name, item.Size, notFound.Message);
            return new MemoryStream([]);
        }

        // Rethrow with the listing metadata so the failure can be understood from the log
        var described = fresh == null
            ? $"item '{item.Name}' listed with size {item.Size?.ToString() ?? "unknown"}"
            : $"item '{item.Name}' listed with size {item.Size?.ToString() ?? "unknown"}, reported by the item itself as size {fresh.Size?.ToString() ?? "unknown"}";
        var enriched = new HttpRequestException($"{notFound.Message} - {described}", notFound, notFound.StatusCode);
        foreach (var key in notFound.Data.Keys)
            enriched.Data[key] = notFound.Data[key];
        throw enriched;
    }

    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", item.Id },
                { "o365:Type", SourceItemType.DriveFile.ToString() },
                { "o365:Name", item.Name },
                { "o365:ETag", item.ETag },
                { "o365:CTag", item.CTag },
                { "o365:MimeType", item.File?.MimeType },
                { "o365:CreatedDateTime", item.CreatedDateTime?.ToGraphTimeString() },
                { "o365:LastModifiedDateTime", item.LastModifiedDateTime?.ToGraphTimeString() },
                { "o365:ParentReference", JsonSerializer.Serialize(item.ParentReference) },
                { "o365:FileSystemInfo", JsonSerializer.Serialize(item.FileSystemInfo) },
                { "o365:DownloadUrl", item.DownloadUrl },
                { "o365:Hashes", JsonSerializer.Serialize(item.File?.Hashes) }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Only items carrying the "shared" facet have sharing links or permissions of their
        // own; everything else inherits from its parent, so asking Graph would cost a request
        // per file and return nothing worth storing.
        if (item.Shared.HasValue)
        {
            try
            {
                var permissions = new List<GraphPermission>();
                await foreach (var perm in provider.OneDriveApi.GetDriveItemPermissionsAsync(drive.Id, item.Id, cancellationToken))
                {
                    permissions.Add(perm);
                }

                if (permissions.Count > 0)
                {
                    metadata["o365:Permissions"] = JsonSerializer.Serialize(permissions);
                }
            }
            catch (Exception ex)
            {
                // Log warning but don't fail the backup if permissions cannot be read
                Log.WriteWarningMessage(LOGTAG, "PermissionReadError", ex, $"Failed to read permissions for file {item.Id}");
            }
        }

        return metadata
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
