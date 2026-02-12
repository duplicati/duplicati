// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;
using File = Google.Apis.Drive.v3.Data.File;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFileSourceEntry(SourceProvider provider, string parentPath, File file)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, file.Name)), file.CreatedTimeDateTimeOffset.HasValue ? file.CreatedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch, file.ModifiedTimeDateTimeOffset.HasValue ? file.ModifiedTimeDateTimeOffset.Value.UtcDateTime : DateTime.UnixEpoch)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveFileMetadataSourceEntry(this.Path, file);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveFilePermissionsSourceEntry(provider, this.Path, file);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveFileContentSourceEntry(provider, this.Path, file);

        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new DriveFileCommentsSourceEntry(provider, this.Path, file);

        if (!GoogleMimeTypes.IsGoogleSite(file.MimeType) && !GoogleMimeTypes.IsShortcut(file.MimeType))
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return new DriveFileRevisionsSourceEntry(provider, this.Path, file);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFile.ToString() },
            { "gsuite:Name", file.Name },
            { "gsuite:Id", file.Id },
            { "gsuite:MimeType", file.MimeType }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
