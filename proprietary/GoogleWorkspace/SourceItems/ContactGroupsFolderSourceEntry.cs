// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ContactGroupsFolderSourceEntry(SourceProvider provider, string parentPath)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Groups")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetPeopleService();
        var request = service.ContactGroups.List();

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var groups = await request.ExecuteAsync(cancellationToken);

            if (groups.ContactGroups != null)
            {
                foreach (var group in groups.ContactGroups)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return new ContactGroupSourceEntry(this.Path, group);
                }
            }
            nextPageToken = groups.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.ContactGroup.ToString() }, // Or UserContactGroups?
            { "gsuite:Name", "Groups" },
            { "gsuite:id", "Groups" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
