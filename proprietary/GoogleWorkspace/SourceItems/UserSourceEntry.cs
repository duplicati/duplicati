// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using User = Google.Apis.Admin.Directory.directory_v1.Data.User;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class UserSourceEntry(SourceProvider provider, string parentPath, User user)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, user.PrimaryEmail)), null, null)
{
    /// <summary>
    /// The primary email of the user, which is the identifier the user-scoped APIs accept.
    /// </summary>
    private readonly string userId = user.PrimaryEmail;

    /// <summary>
    /// Whether the account is suspended or archived. Google keeps the Gmail and Drive data of
    /// such an account readable, but revokes some of its access, so entries below it tolerate
    /// errors that would be reported for an active account.
    /// </summary>
    private readonly bool userIsInactive = SourceProvider.ClassifyUser(user) != SourceProvider.UserCategory.Active;

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Suspended and archived accounts are still backed up, but do not consume a seat.
        var countsAsSeat = SourceProvider.UserCountsAsSeat(user);

        if (!provider.LicenseApprovedForEntry(parentPath, GoogleRootType.Users, user.Id, true, countsAsSeat))
            yield break;

        if (cancellationToken.IsCancellationRequested)
            yield break;

        yield return new UserProfileSourceEntry(provider, this.Path, userId);

        foreach (var type in provider.Options.IncludedUserTypes)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (type == GoogleUserType.Gmail)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, userIsInactive, "Gmail", SourceItemType.UserGmail);
            else if (type == GoogleUserType.Calendar)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, userIsInactive, "Calendar", SourceItemType.UserCalendar);
            else if (type == GoogleUserType.Contacts)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, userIsInactive, "Contacts", SourceItemType.UserContacts);
            else if (type == GoogleUserType.Drive)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, userIsInactive, "Drive", SourceItemType.UserDrive);
            else if (type == GoogleUserType.Tasks)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, userIsInactive, "Tasks", SourceItemType.UserTasks);
            else if (type == GoogleUserType.Keep)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, userIsInactive, "Keep", SourceItemType.UserKeep);
            else if (type == GoogleUserType.Chat)
                yield return new UserTypeSourceEntry(provider, this.Path, userId, userIsInactive, "Chat", SourceItemType.UserChat);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "gsuite:v", "1" },
                { "gsuite:Type", SourceItemType.User.ToString() },
                { "gsuite:Name", userId },
                { "gsuite:Id", userId },
                { "gsuite:Suspended", user.Suspended?.ToString() },
                { "gsuite:Archived", user.Archived?.ToString() },
                { "gsuite:Classification", SourceProvider.ClassifyUser(user).ToString() }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
