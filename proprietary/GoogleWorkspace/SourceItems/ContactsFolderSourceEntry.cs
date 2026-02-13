// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ContactsFolderSourceEntry(SourceProvider provider, string parentPath, string userId)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Contacts")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var service = provider.ApiHelper.GetPeopleService(userId);
        var request = service.People.Connections.List("people/me");
        request.PersonFields = "names,emailAddresses,phoneNumbers,photos";

        string? nextPageToken = null;
        do
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            request.PageToken = nextPageToken;
            var connections = await request.ExecuteAsync(cancellationToken);

            if (connections.Connections != null)
            {
                foreach (var person in connections.Connections)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return new ContactSourceEntry(this.Path, person);
                }
            }
            nextPageToken = connections.NextPageToken;
        } while (!string.IsNullOrEmpty(nextPageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.UserContacts.ToString() },
            { "gsuite:Name", "Contacts" },
            { "gsuite:Id", "Contacts" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
