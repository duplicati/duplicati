// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class SharePointListSourceEntry(SourceProvider provider, string path, GraphSite site, GraphList list)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, list.Id)), list.CreatedDateTime.FromGraphDateTime(), list.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in provider.SharePointListApi.ListListItemsAsync(site.Id, list.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new SharePointListItemSourceEntry(provider, Path, list.Id, site, item);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
        {
            { "o365:v", "1" },
            { "o365:Id", list.Id },
            { "o365:Type", SourceItemType.SharePointList.ToString() },
            { "o365:Name", list.Name ?? list.DisplayName ?? "" },
            { "o365:DisplayName", list.DisplayName ?? "" },
            { "o365:Description", list.Description ?? "" },
            { "o365:WebUrl", list.WebUrl ?? "" },
            { "o365:ListInfo", JsonSerializer.Serialize(list.List) ?? "" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
}
