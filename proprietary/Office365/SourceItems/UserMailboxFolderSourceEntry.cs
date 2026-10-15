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
    /// Reads the messages a delta walk of the folder reports. A full walk streams them, as a
    /// message is reported once. An incremental walk may report a message more than once, for
    /// instance as changed and then as removed, and the later report is the newer state, so its
    /// messages are collected before any is returned and the last report of each wins, in the
    /// order the messages were first reported.
    /// </summary>
    /// <param name="storedToken">The stored delta token, or <c>null</c> for a full walk</param>
    /// <param name="deltaLink">Receives the delta link the walk ends with</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The messages</returns>
    private IAsyncEnumerable<GraphMessage> ReadMessagesAsync(string? storedToken, DeltaLinkHolder deltaLink, CancellationToken cancellationToken)
        => storedToken == null
            ? provider.UserEmailApi.ListMessagesDeltaAsync(user.Id, folder.Id, null, deltaLink, true, cancellationToken)
            : LastReportWinsAsync(provider.UserEmailApi.ListMessagesDeltaAsync(user.Id, folder.Id, storedToken, deltaLink, false, cancellationToken), cancellationToken);

    /// <summary>
    /// Collects the reported messages and returns the last report of each, in the order the
    /// messages were first reported
    /// </summary>
    /// <param name="reports">The reports, possibly several per message</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The newest report of each message</returns>
    private static async IAsyncEnumerable<GraphMessage> LastReportWinsAsync(IAsyncEnumerable<GraphMessage> reports, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var order = new List<string>();
        var latest = new Dictionary<string, GraphMessage>(StringComparer.Ordinal);
        await foreach (var report in reports.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // A report without an id cannot be matched to another, so it stands on its own
            var key = string.IsNullOrEmpty(report.Id) ? $"\0{order.Count}" : report.Id;
            if (!latest.ContainsKey(key))
                order.Add(key);
            latest[key] = report;
        }

        foreach (var key in order)
            yield return latest[key];
    }

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
            var messages = ReadMessagesAsync(stored, deltaLink, cancellationToken).GetAsyncEnumerator(cancellationToken);
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
                        messages = ReadMessagesAsync(null, deltaLink, cancellationToken).GetAsyncEnumerator(cancellationToken);
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
