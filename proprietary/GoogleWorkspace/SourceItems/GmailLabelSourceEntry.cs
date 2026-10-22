// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.ChangeTracking;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Gmail.v1;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// A label folder. It either lists the messages carrying the label with a request to Gmail,
/// or lists a prepared set of changed messages that the mailbox read from its history.
/// </summary>
/// <param name="provider">The provider</param>
/// <param name="userId">The user</param>
/// <param name="parentPath">The path of the mailbox folder</param>
/// <param name="label">The label</param>
/// <param name="service">The Gmail service of the user</param>
/// <param name="changes">The changed messages to list instead of asking Gmail, or <c>null</c> to list the label in full</param>
/// <param name="stateKey">The change tracking key of the mailbox, or <c>null</c> if changes are not tracked</param>
internal class GmailLabelSourceEntry(SourceProvider provider, string userId, string parentPath, Label label, GmailService service, IReadOnlyList<Message>? changes, string? stateKey)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, label.Id)), null, null)
{
    public override IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
    {
        return EnumerateMessages(cancellationToken);
    }

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateMessages([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (changes != null)
        {
            foreach (var message in changes)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new GmailMessageSourceEntry(provider, stateKey, userId, this.Path, message, service, null);
            }

            yield break;
        }

        // A listing or metadata fetch that fails is reported to the mailbox's key, so the
        // history id stored by this run does not claim the label was listed
        await foreach (var message in provider.ChangeTracker.ListWithFailureTrackingAsync(stateKey, ListMessagesAsync(cancellationToken), cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new GmailMessageSourceEntry(provider, stateKey, userId, this.Path, message, service, null);
        }
    }

    /// <summary>
    /// Lists the messages carrying the label with their metadata
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The messages</returns>
    private async IAsyncEnumerable<Message> ListMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = service.Users.Messages.List(userId);
        request.LabelIds = new[] { label.Id };

        do
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Messages != null && response.Messages.Count > 0)
            {
                var messages = await GmailMessageBatch.GetMetadataAsync(service, userId, response.Messages.Select(m => m.Id).ToList(), GoogleRetryHandler.CreateBackOff(),
                    (id, _) => { if (stateKey != null) provider.ChangeTracker.MarkItemFailed(stateKey, id + GmailMailboxWalk.MESSAGE_EXTENSION); }, cancellationToken);

                foreach (var message in messages)
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    yield return message;
                }
            }

            request.PageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(request.PageToken));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string?>
        {
            { "gsuite:v", "1" },
            { "gsuite:Type", SourceItemType.GmailLabel.ToString() },
            { "gsuite:Name", label.Name },
            { "gsuite:Id", label.Id },
            { "gsuite:LabelType", label.Type },
            { "gsuite:LabelListVisibility", label.LabelListVisibility },
            { "gsuite:MessageListVisibility", label.MessageListVisibility }
        };

        // Add color information if present
        if (label.Color != null)
        {
            metadata["gsuite:ColorBackground"] = label.Color.BackgroundColor;
            metadata["gsuite:ColorText"] = label.Color.TextColor;
        }

        return Task.FromResult(metadata
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
