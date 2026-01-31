// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class GroupChannelTabSourceEntry(SourceProvider provider, string path, GraphGroup group, GraphChannel channel, GraphTeamsTab tab)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, tab.Id)), DateTime.UnixEpoch, null)
{
    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", tab.Id },
                { "o365:Type", SourceItemType.GroupChannelTab.ToString() },
                { "o365:DisplayName", tab.DisplayName },
                { "o365:WebUrl", tab.WebUrl },
                { "o365:TeamsAppId", tab.TeamsApp?.Id },
                { "o365:EntityId", tab.Configuration?.EntityId },
                { "o365:ContentUrl", tab.Configuration?.ContentUrl },
                { "o365:RemoveUrl", tab.Configuration?.RemoveUrl },
                { "o365:WebsiteUrl", tab.Configuration?.WebsiteUrl },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "content.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: (ct) => provider.GroupTeamsApi.GetChannelTabStreamAsync(group.Id, channel.Id, tab.Id, ct));
    }
}
