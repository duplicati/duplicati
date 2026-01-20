// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class SharePointListItemAttachmentSourceEntry(SourceProvider provider, string path, string listId, string itemId, GraphSite site, GraphAttachment attachment)
    : StreamResourceEntryBase(SystemIO.IO_OS.PathCombine(path, attachment.Name ?? attachment.Id))
{
    public override DateTime CreatedUtc => DateTime.UnixEpoch;
    public override DateTime LastModificationUtc => DateTime.UnixEpoch;
    public override long Size => attachment.Size ?? 0;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
        => provider.SharePointListApi.GetAttachmentContentStreamAsync(site.WebUrl!, listId, itemId, attachment.Id, cancellationToken);

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
        {
            { "o365:v", "1" },
            { "o365:Id", attachment.Id },
            { "o365:Type", SourceItemType.SharePointListItemAttachment.ToString() },
            { "o365:Name", attachment.Name ?? "" },
            { "o365:ContentType", attachment.ContentType ?? "" },
            { "o365:Size", attachment.Size?.ToString() ?? "0" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
}
