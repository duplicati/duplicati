// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class UserMailboxFolderSourceEntry(SourceProvider provider, GraphUser user, string path, GraphMailFolder folder)
    : MetaEntryBase(Util.AppendDirSeparator(path), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var email in provider.UserEmailApi.ListAllEmailsInFolderAsync(user.Id, folder.Id, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new UserMailboxEmailSourceEntry(provider, this.Path, user, email);
        }

        await foreach (var childFolder in provider.UserEmailApi.ListMailChildFoldersAsync(user.Id, folder.Id, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new UserMailboxFolderSourceEntry(
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
                { "o365:Type", SourceItemType.UserMailboxFolder.ToString() },
                { "o365:Name", folder.DisplayName ?? "" },
                { "o365:DisplayName", folder.DisplayName ?? "" },
                { "o365:ParentFolderId", folder.ParentFolderId ?? "" },
                { "o365:IsHidden", folder.IsHidden?.ToString() ?? "" },
            }
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
}
