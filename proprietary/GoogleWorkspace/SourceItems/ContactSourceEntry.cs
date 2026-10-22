// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// A contact, listed as a folder of its vCard, its details and its photos
/// </summary>
/// <param name="provider">The provider</param>
/// <param name="stateKey">The change tracking key of the user's contacts, so a failed photo download holds the stored token back, or <c>null</c> if changes are not tracked</param>
/// <param name="parentPath">The path of the parent</param>
/// <param name="person">The contact</param>
/// <param name="userIsInactive">Whether the user is suspended or archived</param>
/// <param name="peopleService">The People service of the user</param>
internal class ContactSourceEntry(SourceProvider provider, string? stateKey, string parentPath, Person person, bool userIsInactive, PeopleServiceService peopleService)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, EntryName(person))), null, null)
{
    /// <summary>
    /// The name of the entry of a contact: the last segment of its resource name
    /// </summary>
    /// <param name="person">The contact</param>
    /// <returns>The entry name</returns>
    internal static string EntryName(Person person)
        => person.ResourceName.Split('/').Last();

    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<ContactSourceEntry>();

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new ContactVCFSourceEntry(this.Path, person);
        yield return new ContactDetailsSourceEntry(this.Path, person);

        // Google no longer serves the contact photos of a suspended or archived account, even
        // to the account itself, so the photo entries are not produced for it. The photo URLs
        // are still recorded in the contact details.
        if (userIsInactive)
        {
            if (person.Photos?.Any(p => !string.IsNullOrWhiteSpace(p.Url)) == true)
                Log.WriteVerboseMessage(LOGTAG, "ContactPhotosSkippedForInactiveUser", Strings.ContactPhotosSkippedForInactiveUser(person.ResourceName));
            yield break;
        }

        if (person.Photos != null)
        {
            foreach ((var photo, var index) in person.Photos.Select((x, index) => (x, index)))
            {
                // Skip if it's the default placeholder (if we can detect it)
                // Usually default photos have a specific URL pattern or metadata.
                // For now, let's just include all photos.
                if (cancellationToken.IsCancellationRequested) yield break;
                if (!string.IsNullOrWhiteSpace(photo.Url))
                    yield return new ContactPhotoSourceEntry(provider, stateKey, this.Path, photo, index, peopleService);
            }
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.Contact.ToString() },
            { "gsuite:Name", person.Names?.FirstOrDefault()?.DisplayName ?? person.ResourceName },
            { "gsuite:Id", person.ResourceName },
            { "gsuite:Etag", person.ETag }
        }
        .Where(kv => !string.IsNullOrEmpty(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
