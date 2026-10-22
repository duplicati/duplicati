// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// The contacts of a user. When the provider tracks changes and a sync token is stored for
/// the user, only the contacts created, changed or deleted since the previous backup are
/// listed, and the rest is carried forward by the backup.
/// </summary>
/// <param name="provider">The provider</param>
/// <param name="parentPath">The path of the parent</param>
/// <param name="userId">The user</param>
/// <param name="userIsInactive">Whether the user is suspended or archived</param>
/// <param name="peopleService">The People service of the user</param>
internal class ContactsFolderSourceEntry(SourceProvider provider, string parentPath, string userId, bool userIsInactive, PeopleServiceService peopleService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, "Entries")), null, null)
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<ContactsFolderSourceEntry>();

    /// <summary>
    /// The fields read for every contact. A sync request must carry the same fields as the
    /// full listing it continues.
    /// </summary>
    private const string PERSON_FIELDS = "names,emailAddresses,phoneNumbers,photos";

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tracker = provider.ChangeTracker;
        var useDelta = provider.ContactsDeltaActive;
        var stateKey = useDelta ? ChangeTrackingKeys.Contacts(userId) : null;
        var stored = stateKey == null ? null : tracker.GetState(stateKey);

        // Decide what the carry-forward sees before anything can fail: with a token, the
        // folder is walked incrementally and unmarked paths below it are copied from the
        // previous backup; without one, the whole folder is listed and the copy is wrong
        if (stored != null)
            tracker.MarkIncremental(Path);
        else if (useDelta)
            tracker.MarkExaminedSubtree(Path);

        var completed = false;
        try
        {
            var request = CreateRequest(useDelta, stored);
            ListConnectionsResponse connections;
            try
            {
                connections = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (stored != null && stateKey != null && GoogleServiceErrors.IsPeopleSyncTokenExpired(ex))
            {
                // The token is no longer accepted, so start over with a full walk
                Log.WriteInformationMessage(LOGTAG, "ContactsSyncTokenExpired", Strings.ContactsSyncTokenExpired(userId));
                tracker.MarkLoadedStateInvalid(stateKey);
                tracker.MarkExaminedSubtree(Path);
                stored = null;
                request = CreateRequest(useDelta, null);
                connections = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            string? nextSyncToken = null;
            string? nextPageToken = null;
            do
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                if (!string.IsNullOrEmpty(nextPageToken))
                {
                    request.PageToken = nextPageToken;
                    connections = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }

                foreach (var person in connections.Connections ?? [])
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    if (string.IsNullOrEmpty(person.ResourceName))
                        continue;

                    if (stored != null)
                        tracker.MarkExaminedSubtree(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(this.Path, ContactSourceEntry.EntryName(person))));

                    // A sync walk reports deleted contacts with the deleted flag
                    if (person.Metadata?.Deleted == true)
                        continue;

                    yield return new ContactSourceEntry(provider, stateKey, this.Path, person, userIsInactive, peopleService);
                }

                nextPageToken = connections.NextPageToken;
                if (!string.IsNullOrWhiteSpace(connections.NextSyncToken))
                    nextSyncToken = connections.NextSyncToken;
            } while (!string.IsNullOrEmpty(nextPageToken));

            if (stateKey != null)
            {
                if (nextSyncToken == null)
                    Log.WriteWarningMessage(LOGTAG, "ContactsSyncTokenMissing", null, Strings.ContactsSyncTokenMissing(userId));

                tracker.SetState(stateKey, nextSyncToken ?? string.Empty);
            }

            completed = true;
        }
        finally
        {
            if (!completed && stateKey != null)
                tracker.MarkStateFailed(stateKey);
        }
    }

    /// <summary>
    /// Creates the connections request
    /// </summary>
    /// <param name="requestSyncToken">Whether the listing should end with a sync token</param>
    /// <param name="syncToken">The stored sync token, or <c>null</c> for a full listing</param>
    /// <returns>The request</returns>
    private PeopleResource.ConnectionsResource.ListRequest CreateRequest(bool requestSyncToken, string? syncToken)
    {
        var request = peopleService.People.Connections.List("people/me");
        request.PersonFields = PERSON_FIELDS;
        if (requestSyncToken)
            request.RequestSyncToken = true;
        if (syncToken != null)
            request.SyncToken = syncToken;
        return request;
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.UserContacts.ToString() },
            { "gsuite:Name", "Entries" }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
