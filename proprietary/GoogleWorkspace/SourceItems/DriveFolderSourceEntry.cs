// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFolderSourceEntry(SourceProvider provider, string userId, string parentPath, string name, string folderId)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, name)), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetDriveService();
        var request = service.Files.List();
        request.Q = $"'{folderId}' in parents and trashed = false";
        request.Fields = "nextPageToken, files(id, name, mimeType, createdTime, modifiedTime, size, description, parents)";

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var files = await request.ExecuteAsync(cancellationToken);

            if (files.Files != null)
            {
                foreach (var file in files.Files)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;

                    if (file.MimeType == GoogleMimeTypes.Folder)
                    {
                        yield return new DriveFolderSourceEntry(provider, userId, this.Path, file.Name, file.Id);
                    }
                    else
                    {
                        yield return new DriveFileSourceEntry(provider, this.Path, file);
                    }
                }
            }
            nextPageToken = files.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFolder.ToString() },
            { "gsuite:Name", name },
            { "gsuite:Id", folderId }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
