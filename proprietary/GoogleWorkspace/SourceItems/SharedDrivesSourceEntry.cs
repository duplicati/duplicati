// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class SharedDrivesSourceEntry(SourceProvider provider, string parentPath, string? userId)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Shared Drives")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = userId != null ? provider.ApiHelper.GetDriveService(userId) : provider.ApiHelper.GetDriveService();
        var request = service.Drives.List();

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var drives = await request.ExecuteAsync(cancellationToken);

            if (drives.Drives != null)
            {
                foreach (var drive in drives.Drives)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;

                    if (!provider.LicenseApprovedForEntry(Path, GoogleRootType.SharedDrives, drive.Id))
                        yield break;

                    yield return new SharedDriveSourceEntry(provider, this.Path, userId!, drive);
                }
            }
            nextPageToken = drives.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.SharedDrives.ToString() },
            { "gsuite:Name", "Shared Drives" },
            { "gsuite:Id", "Shared Drives" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
