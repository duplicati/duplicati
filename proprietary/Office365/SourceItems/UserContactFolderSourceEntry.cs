// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class UserContactFolderSourceEntry(SourceProvider provider, GraphUser user, string path, GraphContactFolder folder)
    : MetaEntryBase(Util.AppendDirSeparator(path), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // List contacts in this folder
        await foreach (var contact in provider.ContactsApi.ListContactsInFolderAsync(user.Id, folder.Id, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var ms = new MemoryStream();
            System.Text.Json.JsonSerializer.Serialize(ms, contact);
            var jsonBytes = ms.ToArray();

            yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, contact.Id + ".json"),
                createdUtc: contact.CreatedDateTime.FromGraphDateTime(),
                lastModificationUtc: contact.LastModifiedDateTime.FromGraphDateTime(),
                size: jsonBytes.Length,
                streamFactory: (ct) => Task.FromResult<Stream>(new MemoryStream(jsonBytes)),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                {
                    { "o365:v", "1" },
                    { "o365:Id", contact.Id },
                    { "o365:Type", SourceItemType.UserContact.ToString() },
                    { "o365:Name", contact.DisplayName },
                }
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value)));

            // Contact Photo
            var photoEntry = new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, contact.Id + ".photo"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) => provider.ContactsApi.GetContactPhotoStreamAsync(user.Id, contact.Id, folder.Id, ct),
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                {
                    { "o365:v", "1" },
                    { "o365:Id", contact.Id },
                    { "o365:Type", SourceItemType.UserContactPhoto.ToString() },
                    { "o365:Name", $"{contact.DisplayName} - Photo" },
                }
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));

            if (await photoEntry.ProbeIfExistsAsync(cancellationToken).ConfigureAwait(false))
                yield return photoEntry;
        }

        // List child folders
        await foreach (var childFolder in provider.ContactsApi.ListContactFoldersAsync(user.Id, folder.Id, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new UserContactFolderSourceEntry(
                provider,
                user,
                SystemIO.IO_OS.PathCombine(this.Path, childFolder.Id),
                childFolder);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", folder.Id },
                { "o365:Type", SourceItemType.UserContactFolder.ToString() },
                { "o365:Name", folder.DisplayName ?? "" },
                { "o365:DisplayName", folder.DisplayName ?? "" },
                { "o365:ParentFolderId", folder.ParentFolderId ?? "" },
            }
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
}
