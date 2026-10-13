// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class UserMailboxFolderSourceEntry(SourceProvider provider, GraphUser user, string path, GraphMailFolder folder)
    : MetaEntryBase(Util.AppendDirSeparator(path), null, null)
{
    private static readonly string LOGTAG = Log.LogTagFromType<UserMailboxFolderSourceEntry>();

    /// <summary>
    /// The key the folder's delta link is stored under
    /// </summary>
    private string StateKey => ChangeTrackingKeys.MailFolder(user.Id, folder.ParentFolderId, folder.Id);

    /// <summary>
    /// Lists the messages and child folders. When the provider tracks changes and a delta
    /// link is stored for the folder, only the messages created, changed or removed since
    /// the previous backup are listed, and the rest is carried forward by the backup.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entries</returns>
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tracker = provider.ChangeTracker;
        var useDelta = provider.MailDeltaEnabled && tracker.IsActive;
        var stored = useDelta ? tracker.GetState(StateKey) : null;
        if (string.IsNullOrWhiteSpace(stored))
            stored = null;
        var completed = false;

        // Decide what the carry-forward sees before anything can fail: with a token, the
        // folder is walked incrementally and unmarked paths below it are copied from the
        // previous backup; without one, the whole folder is listed and the copy is wrong
        if (stored != null)
            tracker.MarkIncremental(Path);
        else if (useDelta)
            tracker.MarkExaminedSubtree(Path);

        try
        {
            var deltaLink = new DeltaLinkHolder();
            var messages = provider.UserEmailApi.ListMessagesDeltaAsync(user.Id, folder.Id, stored, deltaLink, cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await messages.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) when (stored != null && APIHelper.IsDeltaResyncRequired(ex))
                    {
                        // The token is no longer accepted, so start over with a full walk of the folder
                        Log.WriteWarningMessage(LOGTAG, "MailDeltaResync", ex, "The stored delta token for mail folder {0} of user {1} is no longer valid, listing the folder in full", folder.Id, user.Id);
                        await messages.DisposeAsync().ConfigureAwait(false);
                        stored = null;
                        tracker.MarkExaminedSubtree(Path);
                        deltaLink = new DeltaLinkHolder();
                        messages = provider.UserEmailApi.ListMessagesDeltaAsync(user.Id, folder.Id, null, deltaLink, cancellationToken).GetAsyncEnumerator(cancellationToken);
                        continue;
                    }

                    if (!hasNext)
                        break;

                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    var email = messages.Current;
                    var messagePath = Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(Path, email.Id));

                    if (email.Removed.HasValue)
                    {
                        if (stored == null)
                            Log.WriteVerboseMessage(LOGTAG, "DeltaRemovedMessageSkipped", "Skipping removed message {0} reported by the delta walk of folder {1}", email.Id, folder.Id);
                        else
                            tracker.MarkExaminedSubtree(messagePath);
                        continue;
                    }

                    if (stored != null)
                        tracker.MarkExaminedSubtree(messagePath);

                    yield return new UserMailboxEmailSourceEntry(provider, Path, user, email, useDelta ? StateKey : null);
                }
            }
            finally
            {
                await messages.DisposeAsync().ConfigureAwait(false);
            }

            var listedChildren = new HashSet<string>(StringComparer.Ordinal);
            await foreach (var childFolder in provider.UserEmailApi.ListMailChildFoldersAsync(user.Id, folder.Id, cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                listedChildren.Add(childFolder.Id);
                var childPath = SystemIO.IO_OS.PathCombine(Path, childFolder.Id);
                if (stored != null)
                    tracker.MarkExamined(Util.AppendDirSeparator(childPath));

                yield return new UserMailboxFolderSourceEntry(provider, user, childPath, childFolder);
            }

            // A child folder the previous backup knew but that is no longer listed was deleted,
            // and its contents must not be carried forward
            if (stored != null)
            {
                var prefix = ChangeTrackingKeys.MailFolderPrefix(user.Id, folder.Id);
                foreach (var key in tracker.GetLoadedKeysWithPrefix(prefix))
                {
                    var childId = key.Substring(prefix.Length);
                    if (!listedChildren.Contains(childId))
                    {
                        Log.WriteVerboseMessage(LOGTAG, "MailFolderRemoved", "Mail folder {0} below {1} of user {2} is no longer listed", childId, folder.Id, user.Id);
                        tracker.MarkExaminedSubtree(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(Path, childId)));
                    }
                }
            }

            if (useDelta)
            {
                var token = DeltaToken.FromMailDeltaLink(deltaLink.DeltaLink);
                if (token == null)
                    Log.WriteWarningMessage(LOGTAG, "MailDeltaTokenMissing", null, "The delta walk of mail folder {0} of user {1} did not end with a delta link carrying a token, so the folder is listed in full next time: {2}", folder.Id, user.Id, deltaLink.DeltaLink);
                else
                    tracker.SetState(StateKey, token);

                completed = true;
            }
        }
        finally
        {
            // A walk that failed keeps the previous token, so the folder is carried
            // forward unchanged and the changes are reported again next time
            if (!completed && stored != null)
                tracker.SetState(StateKey, stored);
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
