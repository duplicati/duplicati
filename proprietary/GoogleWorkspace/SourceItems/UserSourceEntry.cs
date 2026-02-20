// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class UserSourceEntry(SourceProvider provider, string parentPath, string userId)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, userId)), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            yield break;

        yield return new UserProfileSourceEntry(provider, this.Path, userId);

        foreach (var type in provider.Options.IncludedUserTypes)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (type == GoogleUserType.Gmail)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, "Gmail", SourceItemType.UserGmail);
            else if (type == GoogleUserType.Calendar)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, "Calendar", SourceItemType.UserCalendar);
            else if (type == GoogleUserType.Contacts)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, "Contacts", SourceItemType.UserContacts);
            else if (type == GoogleUserType.Drive)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, "Drive", SourceItemType.UserDrive);
            else if (type == GoogleUserType.Tasks)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, "Tasks", SourceItemType.UserTasks);
            else if (type == GoogleUserType.Keep)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, "Keep", SourceItemType.UserKeep);
            else if (type == GoogleUserType.Chat)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, "Chat", SourceItemType.UserChat);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "gsuite:v", "1" },
                { "gsuite:Type", SourceItemType.User.ToString() },
                { "gsuite:Name", userId },
                { "gsuite:Id", userId }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
