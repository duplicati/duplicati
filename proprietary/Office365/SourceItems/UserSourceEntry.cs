// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

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
    private static readonly string LOGTAG = Log.LogTagFromType<UserSourceEntry>();

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Only licensed accounts consume a seat; an unlicensed account, including a shared
        // mailbox without additional storage, does not.
        var countsAsSeat = provider.UserCountsAsSeat(user);

        if (!provider.LicenseApprovedForEntry(parentPath, Office365MetaType.Users, user.Id, true, countsAsSeat))
            yield break;

        foreach (var type in provider.IncludedUserTypes)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new UserTypeSourceEntry(provider, Path, user, type);
        }
    }

    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => new Dictionary<string, string?>
            {
                { "o365:v", "1" },
                { "o365:Id", user.Id },
                { "o365:Type", SourceItemType.User.ToString() },
                { "o365:Name", user.DisplayName ?? "" },
                { "o365:DisplayName", user.DisplayName ?? "" },
                { "o365:UserPrincipalName", user.UserPrincipalName ?? "" },
                { "o365:AccountEnabled", user.AccountEnabled?.ToString() ?? "" },
                // Only the user interface consumes the classification, and resolving it costs a
                // Graph request per user, so a backup does not pay for it.
                { "o365:Classification", provider.EnumerationMode ? await GetUserClassificationAsync(cancellationToken).ConfigureAwait(false) : null },
            }
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    /// <summary>
    /// Gets the classification string for treeview display, using the full classification
    /// (which includes the shared-mailbox distinction) and falling back to the directory-only
    /// classification if it cannot be resolved.
    /// </summary>
    /// <remarks>
    /// The full classification requires a Graph lookup, cached per user. It is resolved here
    /// rather than while enumerating so that the cost is proportional to the entries actually
    /// displayed instead of every entry walked past. Enumerating a folder is not billed for
    /// metadata it never emits.
    /// </remarks>
    private async Task<string> GetUserClassificationAsync(CancellationToken cancellationToken)
    {
        var cached = provider.TryGetCachedUserCategory(user.Id);
        if (cached != null)
            return cached.Value.ToString();

        try
        {
            var category = await provider.ClassifyUserAsync(user, cancellationToken).ConfigureAwait(false);
            return category.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Metadata generation must not fail over a classification lookup.
            Log.WriteVerboseMessage(LOGTAG, "UserClassificationLookupFailed", ex, $"Failed to resolve the classification for user '{user.Id}'; falling back to the directory classification.");
            return SourceProvider.ClassifyUserFromDirectory(user);
        }
    }

    public override Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => Task.FromResult(provider.IncludedUserTypes.Any(x => x.ToString() == filename));
}