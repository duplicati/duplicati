// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class ChatHostedContentSourceEntry(SourceProvider provider, string path, GraphChat chat, GraphChatMessage message)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, "hostedcontent")), null, null)
{
    public async override IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var attachment in provider.ChatApi.ListChatHostedContentsAsync(chat.Id, message.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, attachment.Id + ".json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: async (ct) => await provider.ChatApi.GetChatHostedContentValueStreamAsync(chat.Id, message.Id, attachment.Id, ct).ConfigureAwait(false),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                {
                    ["o365:v"] = "1",
                    ["o365:Id"] = attachment.Id,
                    ["o365:Type"] = SourceItemType.ChatHostedContent.ToString(),
                    ["o365:ContentType"] = attachment.ContentType ?? "",
                }
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
    }
}
