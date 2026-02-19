// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.Office365.SourceItems;

[Flags]
internal enum Office365GroupType
{
    Mailbox = 1,
    Calendar = 2,
    Files = 4,
    Planner = 8,
    Teams = 16,
    Notes = 32
}

internal class GroupSourceEntry(SourceProvider provider, string path, GraphGroup group)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, group.Id)), group.CreatedDateTime.FromGraphDateTime(), null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in MetadataEntries(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return entry;
        }

        foreach (var type in provider.IncludedGroupTypes)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new GroupTypeSourceEntry(provider, Path, group, type);
        }
    }

    private async IAsyncEnumerable<ISourceProviderEntry> MetadataEntries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var res = new StreamResourceEntryFunction[] {
            new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "metadata.json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => provider.GroupApi.GetGroupMetadataStreamAsync(group.Id, ct),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?> {
                    { "o365:Type", SourceItemType.GroupSettings.ToString() },
                    { "o365:Id", group.Id }
                })),

            new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "members.json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => DirectoryEntriesAsStream(provider.GroupApi.ListGroupMembersAsync(group.Id, ct), ct),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?> {
                    { "o365:Type", SourceItemType.GroupMember.ToString() },
                    { "o365:Id", group.Id }
                })),

            new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "owners.json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => DirectoryEntriesAsStream(provider.GroupApi.ListGroupOwnersAsync(group.Id, ct), ct),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?> {
                    { "o365:Type", SourceItemType.GroupOwner.ToString() },
                    { "o365:Id", group.Id }
                })),
        };

        foreach (var entry in res)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return entry;
        }
    }

    private async Task<Stream> DirectoryEntriesAsStream<T>(IAsyncEnumerable<T> items, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, await items.ToListAsync(cancellationToken));
        stream.Position = 0;
        return stream;
    }

    public override Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => Task.FromResult(provider.IncludedGroupTypes.Any(x => x.ToString() == filename));

    override public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", group.Id },
                { "o365:Type", SourceItemType.Group.ToString() },
                { "o365:Name", group.DisplayName },
                { "o365:Description", group.Description },
                { "o365:CreatedDateTime", group.CreatedDateTime.FromGraphDateTime().ToString("o", CultureInfo.InvariantCulture) },
                { "o365:Visibility", group.Visibility },
                { "o365:MailEnabled", group.MailEnabled.ToString() },
                { "o365:SecurityEnabled", group.SecurityEnabled.ToString() },
            }
            .WhereNotNull()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
}
