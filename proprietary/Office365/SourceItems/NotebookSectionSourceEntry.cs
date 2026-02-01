// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class NotebookSectionSourceEntry(SourceProvider provider, string path, GraphOnenoteSection section)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, section.Id)), section.CreatedDateTime.FromGraphDateTime(), section.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var page in provider.OnenoteApi.ListSectionPagesAsync(section.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (page.ContentUrl == null)
                continue;

            yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, page.Id + ".html"),
                createdUtc: page.CreatedDateTime.FromGraphDateTime(),
                lastModificationUtc: page.LastModifiedDateTime.FromGraphDateTime(),
                size: -1,
                streamFactory: (ct) => provider.OnenoteApi.GetOnenotePageContentStreamAsync(page.ContentUrl, ct));
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", section.Id },
                { "o365:Type", SourceItemType.NotebookSection.ToString() },
                { "o365:Name", section.DisplayName ?? "" },
                { "o365:DisplayName", section.DisplayName ?? "" }
            }
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
}
