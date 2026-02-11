// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class SitesSourceEntry(SourceProvider provider, string parentPath)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Sites")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDriveService();
        var request = service.Files.List();
        request.Q = $"mimeType='{GoogleMimeTypes.Site}' and trashed=false";
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;
        // Request all drives so we do not get user drives only
        request.Corpora = "allDrives";

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;

            Google.Apis.Drive.v3.Data.FileList? files = null;
            try
            {
                files = await request.ExecuteAsync(cancellationToken);
            }
            catch
            {
                // Fallback or ignore
                yield break;
            }

            if (files != null && files.Files != null)
            {
                foreach (var file in files.Files)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;

                    if (!provider.LicenseApprovedForEntry(Path, GoogleRootType.Sites, file.Id))
                        yield break;

                    yield return new SiteSourceEntry(this.Path, file);
                }
            }
            nextPageToken = files?.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.MetaRootSites.ToString() },
            { "gsuite:Name", "Sites" },
            { "gsuite:Id", "Sites" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
