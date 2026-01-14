// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal enum Office365UserType
{
    Profile,
    Mailbox,
    Calendar,
    Contacts,
    Tasks,
    Notes,
    Planner,
    Chats
}

internal class UserSourceEntry(SourceProvider provider, string path, GraphUser user)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, user.Id)), user.CreatedDateTime.FromGraphDateTime(), null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
            }
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

    public override Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => Task.FromResult(provider.IncludedUserTypes.Any(x => x.ToString() == filename));
}