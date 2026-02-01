// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal enum ChatEntryType
{
    Message,
    Member
}

internal class ChatSourceEntry(SourceProvider provider, string path, GraphUser user, GraphChat chat)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, chat.Id)), chat.CreatedDateTime.FromGraphDateTime(), chat.LastUpdatedDateTime.FromGraphDateTime())
{
    public async override IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var ms = new MemoryStream();
        System.Text.Json.JsonSerializer.Serialize(ms, chat);
        ms.Seek(0, SeekOrigin.Begin);

        yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "content.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: (ct) => Task.FromResult<Stream>(ms),
            minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", chat.Id },
                { "o365:UserId", user.Id },
                { "o365:Type", SourceItemType.Chat.ToString() },
                { "o365:Name", chat.Topic ?? "" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value)));

        yield return new ChatEntriesSourceEntry(provider, this.Path, chat, ChatEntryType.Message);
        yield return new ChatEntriesSourceEntry(provider, this.Path, chat, ChatEntryType.Member);
    }
}
