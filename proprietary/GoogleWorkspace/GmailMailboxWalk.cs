// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Util;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Lists the label folders and settings of a mailbox. When the provider tracks changes and a
/// history id is stored for the mailbox, the Gmail history since that id is read, the messages
/// it mentions are fetched once and listed under their current labels, and the rest of the
/// mailbox is carried forward by the backup. One history covers every label, so the mailbox
/// stores one id, and the label folders below it are the incremental folders.
/// </summary>
internal static class GmailMailboxWalk
{
    private static readonly string LOGTAG = Log.LogTagFromType(typeof(GmailMailboxWalk));

    /// <summary>
    /// The largest page the history listing allows
    /// </summary>
    private const int HISTORY_PAGE_SIZE = 500;

    /// <summary>
    /// The file extension of a message entry; the touched ids are matched against the last
    /// path segment, which carries it
    /// </summary>
    internal const string MESSAGE_EXTENSION = ".eml";

    /// <summary>
    /// The prefix of the ids Gmail gives user-created labels; system labels have ids such as
    /// <c>INBOX</c> or <c>CATEGORY_PROMOTIONS</c>
    /// </summary>
    private const string USER_LABEL_PREFIX = "Label_";

    /// <summary>
    /// The outcome of reading the history: the changed messages by label, and the history id
    /// the next backup continues from
    /// </summary>
    /// <param name="ByLabel">The changed messages below each label id</param>
    /// <param name="HistoryId">The history id to store</param>
    private sealed record IncrementalPlan(Dictionary<string, List<Message>> ByLabel, string HistoryId);

    /// <summary>
    /// Lists the mailbox
    /// </summary>
    /// <param name="provider">The provider</param>
    /// <param name="userId">The user whose mailbox is listed</param>
    /// <param name="path">The path of the mailbox folder, ending with the separator</param>
    /// <param name="service">The Gmail service of the user</param>
    /// <param name="labels">The labels of the mailbox</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entries</returns>
    public static async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync(SourceProvider provider, string userId, string path, GmailService service, IReadOnlyList<Label> labels, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tracker = provider.ChangeTracker;
        var useDelta = provider.GmailDeltaActive;
        var stateKey = useDelta ? ChangeTrackingKeys.Gmail(userId) : null;
        var stored = stateKey == null ? null : tracker.GetState(stateKey);

        // The mailbox level itself is listed in full on every run: the labels and the settings.
        // A label folder that is no longer listed is dropped with its messages, and the folders
        // that are walked from the history are marked incremental below this mark
        if (useDelta)
            tracker.MarkExaminedSubtree(path);

        // Decide what the carry-forward sees before anything can fail: with a stored id, the
        // label folders are walked incrementally and unmarked paths below them are copied from
        // the previous backup, so a failed history read carries the mailbox forward unchanged
        if (stored != null)
            foreach (var label in labels)
                tracker.MarkIncremental(LabelPath(path, label));

        var completed = false;
        try
        {
            if (stored != null && stateKey != null)
            {
                var plan = await TryReadHistoryAsync(provider, userId, path, service, stateKey, stored, cancellationToken).ConfigureAwait(false);
                if (plan != null)
                {
                    // The labels were listed before the history was read, so a label created
                    // and applied in between is missing from the list while the history
                    // records that put messages below it are consumed by the stored id. Such a
                    // label is fetched now and listed like the others; it has no previous
                    // paths, so marking it incremental this late changes nothing for the
                    // carry-forward. Only a user-created label can appear this way, so a
                    // system label id the listing does not return never costs a second request
                    var known = labels.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                    if (plan.ByLabel.Keys.Any(x => !known.Contains(x) && x.StartsWith(USER_LABEL_PREFIX, StringComparison.Ordinal)))
                    {
                        var response = await service.Users.Labels.List(userId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
                        var added = (response.Labels ?? []).Where(x => !string.IsNullOrEmpty(x.Id) && !known.Contains(x.Id)).ToList();
                        foreach (var label in added)
                            tracker.MarkIncremental(LabelPath(path, label));

                        Log.WriteVerboseMessage(LOGTAG, "GmailLabelsRelisted", "The history of user {0} mentioned labels that were not listed, {1} new label(s) were found", userId, added.Count);
                        labels = [.. labels, .. added];
                    }

                    foreach (var label in labels)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            yield break;

                        yield return new GmailLabelSourceEntry(provider, userId, path, label, service, plan.ByLabel.GetValueOrDefault(label.Id) ?? [], stateKey);
                    }

                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    yield return new GmailSettingsSourceEntry(userId, path, service);
                    tracker.SetState(stateKey, plan.HistoryId);
                    completed = true;
                    yield break;
                }
            }

            // The history could not be used; every label is listed in full now, and a full
            // listing inside the incremental mark is honored over it
            if (stored != null)
                foreach (var label in labels)
                    tracker.MarkExaminedSubtree(LabelPath(path, label));

            // Take the history id before the walk, so anything that changes during the walk is
            // reported by the next history read; a change reported twice is acceptable, a missed one is not
            string? historyId = null;
            if (stateKey != null)
            {
                var profile = await service.Users.GetProfile(userId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
                historyId = profile.HistoryId?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            foreach (var label in labels)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new GmailLabelSourceEntry(provider, userId, path, label, service, null, stateKey);
            }

            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new GmailSettingsSourceEntry(userId, path, service);

            if (stateKey != null)
            {
                if (historyId == null)
                    Log.WriteWarningMessage(LOGTAG, "GmailHistoryIdMissing", null, Strings.GmailHistoryIdMissing(userId));
                tracker.SetState(stateKey, historyId ?? string.Empty);
            }

            completed = true;
        }
        finally
        {
            // A walk that failed keeps the previous id, so the mailbox is carried forward
            // unchanged and the changes are reported again next time; after repeated failures
            // the tracker resets the state so the mailbox is listed in full
            if (!completed && stateKey != null)
                tracker.MarkStateFailed(stateKey);
        }
    }

    /// <summary>
    /// The path of a label folder
    /// </summary>
    /// <param name="mailboxPath">The path of the mailbox folder</param>
    /// <param name="label">The label</param>
    /// <returns>The folder path, ending with the separator</returns>
    private static string LabelPath(string mailboxPath, Label label)
        => Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(mailboxPath, label.Id));

    /// <summary>
    /// Reads the history since the stored id and fetches the messages it mentions
    /// </summary>
    /// <param name="provider">The provider</param>
    /// <param name="userId">The user</param>
    /// <param name="path">The path of the mailbox folder, which the touched ids are scoped to</param>
    /// <param name="service">The Gmail service</param>
    /// <param name="stateKey">The state key of the mailbox</param>
    /// <param name="stored">The stored history id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The plan, or <c>null</c> if the mailbox must be listed in full</returns>
    private static async Task<IncrementalPlan?> TryReadHistoryAsync(SourceProvider provider, string userId, string path, GmailService service, string stateKey, string stored, CancellationToken cancellationToken)
    {
        var tracker = provider.ChangeTracker;
        if (!ulong.TryParse(stored, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var startHistoryId))
        {
            Log.WriteWarningMessage(LOGTAG, "GmailHistoryIdUnreadable", null, Strings.GmailHistoryExpired(userId));
            tracker.MarkLoadedStateInvalid(stateKey);
            return null;
        }

        var touched = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        string? newHistoryId = null;
        string? pageToken = null;

        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = service.Users.History.List(userId);
                request.StartHistoryId = startHistoryId;
                request.MaxResults = HISTORY_PAGE_SIZE;
                request.HistoryTypesList = new Repeatable<UsersResource.HistoryResource.ListRequest.HistoryTypesEnum>(
                [
                    UsersResource.HistoryResource.ListRequest.HistoryTypesEnum.MessageAdded,
                    UsersResource.HistoryResource.ListRequest.HistoryTypesEnum.MessageDeleted,
                    UsersResource.HistoryResource.ListRequest.HistoryTypesEnum.LabelAdded,
                    UsersResource.HistoryResource.ListRequest.HistoryTypesEnum.LabelRemoved,
                ]);
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (response.HistoryId.HasValue)
                    newHistoryId = response.HistoryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

                foreach (var record in response.History ?? [])
                {
                    var ids = (record.Messages ?? []).Select(m => m.Id)
                        .Concat((record.MessagesAdded ?? []).Select(m => m.Message?.Id))
                        .Concat((record.MessagesDeleted ?? []).Select(m => m.Message?.Id))
                        .Concat((record.LabelsAdded ?? []).Select(m => m.Message?.Id))
                        .Concat((record.LabelsRemoved ?? []).Select(m => m.Message?.Id));

                    foreach (var id in ids)
                        if (!string.IsNullOrEmpty(id) && touched.Add(id))
                            order.Add(id);
                }

                pageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (Exception ex) when (GoogleServiceErrors.IsGmailHistoryExpired(ex))
        {
            // The id is no longer accepted, so start over with a full walk of the mailbox
            Log.WriteInformationMessage(LOGTAG, "GmailHistoryExpired", Strings.GmailHistoryExpired(userId));
            tracker.MarkLoadedStateInvalid(stateKey);
            return null;
        }

        if (newHistoryId == null)
        {
            // Gmail always reports the current id with the history; without it there is
            // nothing to continue from, so the mailbox is listed in full next time
            Log.WriteWarningMessage(LOGTAG, "GmailHistoryIdMissing", null, Strings.GmailHistoryIdMissing(userId));
            tracker.MarkLoadedStateInvalid(stateKey);
            return null;
        }

        // A message that was deleted answers not found and is skipped; a message whose metadata
        // cannot be read for another reason holds the id back, so it is reported again
        var unreadable = new HashSet<string>(StringComparer.Ordinal);
        var messages = await GmailMessageBatch.GetMetadataAsync(service, userId, order, GoogleRetryHandler.CreateBackOff(), (id, _) =>
        {
            unreadable.Add(id);
            tracker.MarkItemFailed(stateKey, id + MESSAGE_EXTENSION);
        }, cancellationToken).ConfigureAwait(false);

        // Every previous path of a touched message counts as examined, whatever label it was
        // under: the message is listed again below its current labels, and the paths that are
        // not listed again are dropped. This is what removes a deleted message, or one that
        // lost a label, without knowing which labels it had. A message that could not be read
        // is not listed anywhere, so its previous paths are left alone and carried forward
        foreach (var id in order)
            if (!unreadable.Contains(id))
                tracker.MarkIdTouched(path, id + MESSAGE_EXTENSION);

        var byLabel = new Dictionary<string, List<Message>>(StringComparer.Ordinal);
        foreach (var message in messages)
            foreach (var labelId in message.LabelIds ?? [])
            {
                if (!byLabel.TryGetValue(labelId, out var list))
                    byLabel[labelId] = list = [];
                list.Add(message);
            }

        Log.WriteVerboseMessage(LOGTAG, "GmailHistoryRead", "The history of user {0} mentioned {1} messages, of which {2} still exist and {3} could not be read", userId, order.Count, messages.Count, unreadable.Count);
        return new IncrementalPlan(byLabel, newHistoryId);
    }
}
