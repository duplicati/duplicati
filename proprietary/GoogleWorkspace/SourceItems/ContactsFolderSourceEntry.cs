// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.PeopleService.v1;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ContactsFolderSourceEntry(string parentPath, PeopleServiceService peopleService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Contacts")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = peopleService.People.Connections.List("people/me");
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
