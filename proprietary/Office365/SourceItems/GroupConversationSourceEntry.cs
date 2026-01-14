// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class GroupConversationSourceEntry(SourceProvider provider, GraphGroup group, string path, GraphConversation conversation)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, conversation.Id)), null, conversation.LastDeliveredDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var thread in provider.GroupConversationApi.ListConversationThreadsAsync(group.Id, conversation.Id, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new GroupConversationThreadSourceEntry(provider, this.Path, group, thread);
        }
    }

    override public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", conversation.Id },
                { "o365:Type", SourceItemType.GroupConversation.ToString() },
                { "o365:Name", conversation.Topic },
                { "o365:Topic", conversation.Topic },
                { "o365:HasAttachments", conversation.HasAttachments.ToString() },
                { "o365:LastDeliveredDateTime", conversation.LastDeliveredDateTime.FromGraphDateTime().ToString("o") }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
