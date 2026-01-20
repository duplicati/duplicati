// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class GroupTypeSourceEntry(SourceProvider provider, string path, GraphGroup group, Office365GroupType groupType)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, groupType.ToString().ToLowerInvariant())), group.CreatedDateTime.FromGraphDateTime(), null)
{
    private static readonly string LOGTAG = Log.LogTagFromType<GroupTypeSourceEntry>();

    public override bool IsMetaEntry
    {
        get
        {
            // Minor hack to skip Teams meta entry if the group is not a Team
            if (groupType != Office365GroupType.Teams)
                return false;

            // Skip if the group is not a Team
            if (!group.ResourceProvisioningOptions?.Contains("Team", StringComparer.OrdinalIgnoreCase) == true)
                return true;

            return false;
        }
    }


    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var entries = groupType switch
        {
            Office365GroupType.Mailbox => MailboxEntries(cancellationToken),
            Office365GroupType.Calendar => CalendarEntries(cancellationToken),
            Office365GroupType.Files => FilesEntries(cancellationToken),
            Office365GroupType.Planner => PlannerEntries(cancellationToken),
            Office365GroupType.Teams => TeamsEntries(cancellationToken),

            _ => null
        };

        if (entries is null)
        {
            Log.WriteWarningMessage(LOGTAG, "UnknownGroupType", null, $"Unknown group type '{groupType}' for group '{group.Id}'");
            yield break;
        }

        await foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            // Probe if the entry exists, if it is a stream resource
            if (entry is StreamResourceEntryFunction sref)
                if (!await sref.ProbeIfExistsAsync(cancellationToken).ConfigureAwait(false))
                    continue;

            yield return entry;
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> MailboxEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if ((group.GroupTypes?.Contains("Unified") ?? false) && (group.MailEnabled ?? false))
        {
            await foreach (var entry in provider.GroupConversationApi.ListGroupConversationsAsync(group.Id, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new GroupConversationSourceEntry(provider, group, this.Path, entry);
            }
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> CalendarEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "metadata.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: (ct) => provider.GroupCalendarApi.GetGroupCalendarStreamAsync(group.Id, ct),
            minorMetadataFactory: async (ct) =>
            {
                var calendar = await provider.GroupCalendarApi.GetGroupCalendarAsync(group.Id, ct).ConfigureAwait(false);
                return new Dictionary<string, string?>()
                {
                    { "o365:v", "1" },
                    { "o365:Id", calendar.Id },
                    { "o365:Type", SourceItemType.GroupCalendar.ToString() },
                    { "o365:Name", calendar.Name ?? "" }
                };
            });

        yield return new GroupCalendarEventsSourceEntry(provider, this.Path, group);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> FilesEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var drive in provider.GroupDriveApi.ListGroupDrivesAsync(group.Id, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new DriveSourceEntry(provider, this.Path, drive);
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> PlannerEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var plan in provider.GroupPlannerApi.ListGroupPlannerPlansAsync(group.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new PlannerPlanSourceEntry(provider, this.Path, plan);
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> TeamsEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Skip if the group is not a Team
        if (!group.ResourceProvisioningOptions?.Contains("Team", StringComparer.OrdinalIgnoreCase) == true)
            yield break;

        // It can be provisioned as a Team but not have a Team yet
        if (!await provider.GroupTeamsApi.IsGroupTeamAsync(group.Id, cancellationToken))
            yield break;

        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "metadata.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: (ct) => provider.GroupTeamsApi.GetTeamMetadataStreamAsync(group.Id, ct));

        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "members.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: async (ct) =>
            {
                var ms = new MemoryStream();
                await JsonSerializer.SerializeAsync(ms, provider.GroupTeamsApi.ListTeamMembersAsync(group.Id, ct).ToListAsync(ct), cancellationToken: ct);
                ms.Position = 0;
                return ms;
            });

        await foreach (var channel in provider.GroupTeamsApi.ListTeamChannelsAsync(group.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new GroupChannelSourceEntry(provider, this.Path, group, channel);
        }

        await foreach (var app in provider.GroupTeamsApi.ListTeamInstalledAppsAsync(group.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new GroupInstalledAppSourceEntry(provider, this.Path, group, app);
        }

    }
}
