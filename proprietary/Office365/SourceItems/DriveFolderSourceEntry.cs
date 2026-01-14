// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class DriveFolderSourceEntry(SourceProvider provider, string path, GraphDrive drive, GraphDriveItem item)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, item.Id)), item.CreatedDateTime.FromGraphDateTime(), item.LastModifiedDateTime.FromGraphDateTime())
{
    private static readonly string LOGTAG = Log.LogTagFromType<DriveFolderSourceEntry>();
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var child in provider.OneDriveApi.ListDriveFolderChildrenAsync(drive.Id, item.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (child.Deleted != null)
                continue;
            if (child.Folder != null)
                yield return new DriveFolderSourceEntry(provider, this.Path, drive, child);
            else if (child.File != null)
                yield return new DriveFileSourceEntry(provider, this.Path, drive, child);
            else
                Log.WriteWarningMessage(LOGTAG, "UnknownDriveItemType", null, $"Unknown drive item type: {item.Id}");
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", item.Id },
                { "o365:Type", SourceItemType.DriveFolder.ToString() },
                { "o365:Name", item.Name ?? "" },
                { "o365:FolderChildCount", item.Folder?.ChildCount.ToString() ?? "" },
                { "o365:ETag", item.ETag ?? "" },
                { "o365:CTag", item.CTag ?? "" },
                { "o365:ParentReference", JsonSerializer.Serialize(item.ParentReference) ?? "" },
                { "o365:FileSystemInfo", JsonSerializer.Serialize(item.FileSystemInfo) ?? "" },
                { "o365:DownloadUrl", item.DownloadUrl ?? "" }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override Task<bool> FileExists(string filename, CancellationToken cancellationToken)
    {
        // TODO: Implement this
        return Task.FromResult(false);
    }
}
