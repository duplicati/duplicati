// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class DriveSourceEntry(SourceProvider provider, string path, GraphDrive drive)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, drive.Id)), drive.CreatedDateTime.FromGraphDateTime(), drive.LastModifiedDateTime.FromGraphDateTime())
{
    private static readonly string LOGTAG = Log.LogTagFromType<DriveSourceEntry>();

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in provider.OneDriveApi.ListDriveRootChildrenAsync(drive.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (item.Deleted != null)
                continue;

            if (item.Folder != null)
                yield return new DriveFolderSourceEntry(provider, Path, drive, item);
            else if (item.File != null)
                yield return new DriveFileSourceEntry(provider, Path, drive, item);
            else
                Log.WriteWarningMessage(LOGTAG, "UnknownDriveItemType", null, $"Unknown drive item type: {item.Id}");
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", drive.Id },
                { "o365:Type", SourceItemType.Drive.ToString() },
                { "o365:Name", drive.Name ?? "" },
                { "o365:Description", drive.Description ?? "" },
                { "o365:WebUrl", drive.WebUrl ?? "" },
                { "o365:CreatedBy", JsonSerializer.Serialize(drive.CreatedBy) ?? "" },
                { "o365:LastModifiedBy", JsonSerializer.Serialize(drive.LastModifiedBy) ?? "" },
                { "o365:DriveType", drive.DriveType ?? "" },
                { "o365:Owner", JsonSerializer.Serialize(drive.Owner) ?? "" },
                { "o365:Quota", JsonSerializer.Serialize(drive.Quota) ?? "" },
                { "o365:SharePointIds", JsonSerializer.Serialize(drive.SharePointIds) ?? "" },
                { "o365:System", JsonSerializer.Serialize(drive.System) ?? "" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
