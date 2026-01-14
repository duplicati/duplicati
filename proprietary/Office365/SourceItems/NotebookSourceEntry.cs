// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class NotebookSourceEntry(SourceProvider provider, string path, GraphUser user, GraphNotebook notebook)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, notebook.Id)), notebook.CreatedDateTime.FromGraphDateTime(), notebook.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var section in provider.OnenoteApi.ListNotebookSectionGroupsAsync(notebook.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new NotebookSectionGroupSourceEntry(provider, this.Path, user, notebook, section);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", notebook.Id },
                { "o365:Type", SourceItemType.Notebook.ToString() },
                { "o365:Name", notebook.DisplayName ?? "" },
                { "o365:DisplayName", notebook.DisplayName ?? "" }
            }
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
}
