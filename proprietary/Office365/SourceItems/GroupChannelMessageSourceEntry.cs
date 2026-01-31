// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class GroupChannelMessageSourceEntry(SourceProvider provider, string path, GraphGroup group, GraphChannel channel, GraphChannelMessage message)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, message.Id)), message.CreatedDateTime.FromGraphDateTime(), message.LastModifiedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "content.json"),
            createdUtc: message.CreatedDateTime.FromGraphDateTime(),
            lastModificationUtc: message.LastModifiedDateTime.FromGraphDateTime(),
            size: -1,
            streamFactory: (ct) => provider.GroupTeamsApi.GetChannelMessageStreamAsync(group.Id, channel.Id, message.Id, ct),
            minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
            {
                    { "o365:v", "1" },
                    { "o365:Id", message.Id },
                    { "o365:Type", SourceItemType.GroupChannelMessage.ToString() },
                    { "o365:Name", $"{message.From?.DisplayName} - {message.Subject}" },
                    { "o365:Subject", message.Subject },
                    { "o365:From", message.From?.ToString() ?? "" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value)));

        yield return new GroupChannelMessageReplySourceEntry(provider, this.Path, group, channel, message);
    }
}
