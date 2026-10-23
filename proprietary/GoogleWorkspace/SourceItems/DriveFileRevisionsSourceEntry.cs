// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.ChangeTracking;
using System.Runtime.CompilerServices;
using File = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class DriveFileRevisionsSourceEntry(SourceProvider provider, string? stateKey, string parentPath, File file, DriveService driveService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Revisions")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var revision in provider.ChangeTracker.ListItemWithFailureTrackingAsync(stateKey, this.Path, ListRevisionsAsync(cancellationToken), cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return new DriveFileRevisionContentSourceEntry(provider, stateKey, this.Path, file, revision, driveService);
        }
    }

    /// <summary>
    /// Lists the revisions of the file
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The revisions</returns>
    private async IAsyncEnumerable<Revision> ListRevisionsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = driveService.Revisions.List(file.Id);
        var revisions = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        foreach (var revision in revisions.Revisions ?? [])
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            yield return revision;
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.DriveFileRevision.ToString() }, // Folder
            { "gsuite:Name", "Revisions" },
            { "gsuite:Id", file.Id + "/revisions" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
