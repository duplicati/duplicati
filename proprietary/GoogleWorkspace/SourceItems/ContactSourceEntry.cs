// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Google.Apis.PeopleService.v1.Data;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class ContactSourceEntry(string parentPath, Person person)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, person.Names?.FirstOrDefault()?.DisplayName ?? person.ResourceName)), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) yield break;
        yield return new ContactFileSourceEntry(this.Path, person);

        if (person.Photos != null)
        {
            foreach (var photo in person.Photos)
            {
                // Skip if it's the default placeholder (if we can detect it)
                // Usually default photos have a specific URL pattern or metadata.
                // For now, let's just include all photos.
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return new ContactPhotoSourceEntry(this.Path, photo);
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
