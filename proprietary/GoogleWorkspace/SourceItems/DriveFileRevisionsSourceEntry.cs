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
    /// The fields requested for every listed revision. Drive's default set has no size and
    /// no export links, and the size is what the backup needs before it reads the content
    /// </summary>
    internal const string REVISION_FIELDS = "id, mimeType, modifiedTime, size, keepForever, originalFilename, md5Checksum, exportLinks";

    /// <summary>
    /// The number of revisions asked for per page, the most Drive allows
    /// </summary>
    private const int PAGE_SIZE = 1000;

    /// <summary>
    /// Lists the revisions of the file, all pages of them
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The revisions</returns>
    private async IAsyncEnumerable<Revision> ListRevisionsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = driveService.Revisions.List(file.Id);
        request.Fields = $"nextPageToken, revisions({REVISION_FIELDS})";
        request.PageSize = PAGE_SIZE;

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var revisions = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            foreach (var revision in revisions.Revisions ?? [])
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                if (string.IsNullOrEmpty(revision.Id))
                    continue;

                yield return revision;
            }

            nextPageToken = revisions.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
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
