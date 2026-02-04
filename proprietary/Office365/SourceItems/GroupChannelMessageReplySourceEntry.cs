// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class GroupChannelMessageReplySourceEntry(SourceProvider provider, string path, GraphGroup group, GraphChannel channel, GraphChannelMessage message)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, "replies")), message.CreatedDateTime.FromGraphDateTime(), message.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var reply in provider.GroupTeamsApi.ListChannelMessageRepliesAsync(group.Id, channel.Id, message.Id, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new StreamResourceEntryFunction(
                SystemIO.IO_OS.PathCombine(this.Path, reply.Id),
                createdUtc: reply.CreatedDateTime.FromGraphDateTime(),
                lastModificationUtc: reply.LastModifiedDateTime.FromGraphDateTime(),
                size: -1,
                streamFactory: (ct) => provider.GroupTeamsApi.GetChannelMessageReplyStreamAsync(group.Id, channel.Id, message.Id, reply.Id, ct),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                {
                    { "o365:v", "1" },
                    { "o365:Id", reply.Id },
                    { "o365:Type", SourceItemType.GroupChannelMessageReply.ToString() },
                    { "o365:Name", $"Re: {reply.From?.DisplayName} - {reply.Subject}" },
                    { "o365:Subject", reply.Subject },
                    { "o365:From", reply.From?.ToString() ?? "" },
                }
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
    }
}