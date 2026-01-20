// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class GroupInstalledAppSourceEntry(SourceProvider provider, string path, GraphGroup group, GraphTeamsAppInstallation app)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, app.Id)), DateTime.UnixEpoch, null)
{
    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", app.Id },
                { "o365:Type", SourceItemType.GroupInstalledApp.ToString() },
                { "o365:TeamsAppId", app.TeamsApp?.Id },
                { "o365:TeamsAppDisplayName", app.TeamsApp?.DisplayName },
                { "o365:TeamsAppDistributionMethod", app.TeamsApp?.DistributionMethod },
                { "o365:TeamsAppDefinitionId", app.TeamsAppDefinition?.Id },
                { "o365:TeamsAppDefinitionDisplayName", app.TeamsAppDefinition?.DisplayName },
                { "o365:TeamsAppDefinitionVersion", app.TeamsAppDefinition?.Version },
                { "o365:TeamsAppDefinitionTeamsAppId", app.TeamsAppDefinition?.TeamsAppId },
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
            streamFactory: (ct) => provider.GroupTeamsApi.GetTeamInstalledAppStreamAsync(group.Id, app.Id, ct));
    }
}
