using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class GroupCalendarEventsSourceEntry(SourceProvider provider, string path, GraphGroup group)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, "events")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in provider.GroupCalendarApi.ListGroupEventsAsync(group.Id, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new StreamResourceEntryFunction(
                SystemIO.IO_OS.PathCombine(this.Path, entry.Id + ".json"),
                createdUtc: entry.CreatedDateTime.FromGraphDateTime(),
                lastModificationUtc: entry.LastModifiedDateTime.FromGraphDateTime(),
                size: -1,
                streamFactory: (ct) => provider.GroupCalendarApi.GetGroupEventStreamAsync(group.Id, entry.Id, ct),
                minorMetadataFactory: ct => Task.FromResult(new Dictionary<string, string?>
                {
                    { "o365:v", "1" },
                    { "o365:Id", entry.Id },
                    { "o365:Type", SourceItemType.GroupCalendarEvent.ToString() },
                    { "o365:Subject", entry.Subject ?? "" },
                    { "o365:Start", entry.Start?.ToString() ?? "" },
                    { "o365:End", entry.End?.ToString() ?? "" },
                }.Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
    }
}
