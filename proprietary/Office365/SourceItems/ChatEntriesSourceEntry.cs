// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class ChatEntriesSourceEntry(SourceProvider provider, string path, GraphChat chat, ChatEntryType type)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, type.ToString().ToLowerInvariant())), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (type == ChatEntryType.Message)
        {
            await foreach (var message in provider.ChatApi.ListChatMessagesAsync(chat.Id, cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new ChatMessageSourceEntry(provider, this.Path, chat, message);
            }
        }
        else if (type == ChatEntryType.Member)
        {
            await foreach (var member in provider.ChatApi.ListChatMembersAsync(chat.Id, cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, member.Id + ".json"),
                    createdUtc: DateTime.UnixEpoch,
                    lastModificationUtc: DateTime.UnixEpoch,
                    size: -1,
                    streamFactory: async (ct) =>
                    {
                        var ms = new MemoryStream();
                        await System.Text.Json.JsonSerializer.SerializeAsync(ms, member, cancellationToken: ct).ConfigureAwait(false);
                        ms.Seek(0, SeekOrigin.Begin);
                        return ms;
                    },
                    minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                    {
                        { "o365:v", "1" },
                        { "o365:Id", member.Id },
                        { "o365:Type", SourceItemType.ChatMember.ToString() },
                    }
                    .Where(kv => !string.IsNullOrEmpty(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value)));
            }
        }
        else
            throw new NotImplementedException();
    }
}
