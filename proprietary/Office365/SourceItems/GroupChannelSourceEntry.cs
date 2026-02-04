// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Globalization;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class GroupChannelSourceEntry(SourceProvider provider, string path, GraphGroup group, GraphChannel channel)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, channel.Id)), channel.CreatedDateTime.FromGraphDateTime(), null)
{
    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", channel.Id },
                { "o365:GroupId", group.Id },
                { "o365:Type", SourceItemType.GroupChannel.ToString() },
                { "o365:Name", channel.DisplayName },
                { "o365:Description", channel.Description },
                { "o365:MembershipType",channel.MembershipType },
                { "o365:CreatedDateTime", channel.CreatedDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "content.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: (ct) => provider.GroupTeamsApi.GetTeamChannelStreamAsync(group.Id, channel.Id, ct));

        await foreach (var message in provider.GroupTeamsApi.ListChannelMessagesAsync(group.Id, channel.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new GroupChannelMessageSourceEntry(provider, this.Path, group, channel, message);

        }

        await foreach (var tab in provider.GroupTeamsApi.ListChannelTabsAsync(group.Id, channel.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new GroupChannelTabSourceEntry(provider, this.Path, group, channel, tab);
        }
    }
}
