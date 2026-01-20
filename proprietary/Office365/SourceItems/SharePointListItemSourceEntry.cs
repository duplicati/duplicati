// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class SharePointListItemSourceEntry(SourceProvider provider, string path, string listId, GraphSite site, GraphListItem item)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, item.Id)), item.CreatedDateTime.FromGraphDateTime(), item.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "content.json"),
            createdUtc: CreatedUtc,
            lastModificationUtc: LastModificationUtc,
            size: -1,
            streamFactory: (ct) =>
            {
                var json = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });
                return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
            }
        );

        if (string.IsNullOrWhiteSpace(site.WebUrl))
            yield break;

        // TODO: This requires specific authentication
        // await foreach (var attachment in provider.SharePointListApi.ListListItemAttachmentsAsync(site.WebUrl, listId, item.Id, cancellationToken).ConfigureAwait(false))
        // {
        //     if (cancellationToken.IsCancellationRequested)
        //         yield break;

        //     yield return new SharePointListItemAttachmentSourceEntry(provider, Path, listId, item.Id, site, attachment);
        // }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
        {
            { "o365:v", "1" },
            { "o365:Id", item.Id },
            { "o365:Type", SourceItemType.SharePointListItem.ToString() },
            { "o365:WebUrl", item.WebUrl ?? "" },
            { "o365:ContentType", item.ContentType?.Name ?? "" },
            { "o365:ContentTypeId", item.ContentType?.Id ?? "" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
}
