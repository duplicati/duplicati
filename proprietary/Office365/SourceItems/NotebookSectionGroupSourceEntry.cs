// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class NotebookSectionGroupSourceEntry(SourceProvider provider, string path, GraphOnenoteSectionGroup sectionGroup)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, sectionGroup.Id)), sectionGroup.CreatedDateTime.FromGraphDateTime(), sectionGroup.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var section in provider.OnenoteApi.ListSectionGroupSectionsAsync(sectionGroup.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new NotebookSectionSourceEntry(provider, this.Path, section);
        }

        await foreach (var childSectionGroup in provider.OnenoteApi.ListSectionGroupSectionGroupsAsync(sectionGroup.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new NotebookSectionGroupSourceEntry(provider, this.Path, childSectionGroup);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
        {
            { "o365:v", "1" },
            { "o365:Id", sectionGroup.Id },
            { "o365:Type", SourceItemType.NotebookSectionGroup.ToString() },
            { "o365:Name", sectionGroup.DisplayName ?? "" },
            { "o365:DisplayName", sectionGroup.DisplayName ?? "" }
        }
        .WhereNotNull()
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

}
