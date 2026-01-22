// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class ChatMessageSourceEntry(SourceProvider provider, string path, GraphChat chat, GraphChatMessage message)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, message.Id)), message.CreatedDateTime.FromGraphDateTime(), message.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "content.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: async (ct) =>
            {
                var ms = new MemoryStream();
                await System.Text.Json.JsonSerializer.SerializeAsync(ms, message, cancellationToken: ct).ConfigureAwait(false);
                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            },
            minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", message.Id },
                { "o365:Type", SourceItemType.ChatMessage.ToString() },
                { "o365:Name", message.Subject ?? "" },
                { "o365:From", message.From?.DisplayName ?? "" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value)));

        yield return new ChatHostedContentSourceEntry(provider, this.Path, chat, message);
    }
}
