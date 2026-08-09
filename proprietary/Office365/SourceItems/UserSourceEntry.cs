// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

[Flags]
internal enum Office365UserType
{
    Profile = 1,
    Mailbox = 2,
    Calendar = 4,
    Contacts = 8,
    Tasks = 16,
    Notes = 32,
    Planner = 64,
    Chats = 128
}

internal class UserSourceEntry(SourceProvider provider, string parentPath, GraphUser user)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, user.Id)), user.CreatedDateTime.FromGraphDateTime(), null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Shared mailboxes without additional storage do not consume a seat.
        var countsAsSeat = await provider.UserCountsAsSeatAsync(user, cancellationToken).ConfigureAwait(false);

        if (!provider.LicenseApprovedForEntry(parentPath, Office365MetaType.Users, user.Id, true, countsAsSeat))
            yield break;

        foreach (var type in provider.IncludedUserTypes)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new UserTypeSourceEntry(provider, Path, user, type);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", user.Id },
                { "o365:Type", SourceItemType.User.ToString() },
                { "o365:Name", user.DisplayName ?? "" },
                { "o365:DisplayName", user.DisplayName ?? "" },
                { "o365:UserPrincipalName", user.UserPrincipalName ?? "" },
                { "o365:AccountEnabled", user.AccountEnabled?.ToString() ?? "" },
                { "o365:Classification", GetUserClassification() },
            }
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

    /// <summary>
    /// Gets the classification string for treeview display, preferring the already-resolved
    /// seat classification (which includes the shared-mailbox distinction) and never
    /// triggering an extra API call.
    /// </summary>
    private string GetUserClassification()
    {
        var cached = provider.TryGetCachedUserSeatCategory(user.Id);
        return cached?.ToString() ?? SourceProvider.ClassifyUserFromDirectory(user);
    }

    public override Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => Task.FromResult(provider.IncludedUserTypes.Any(x => x.ToString() == filename));
}