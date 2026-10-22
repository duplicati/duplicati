// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net;
using Duplicati.Library.Logging;
using Google;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Requests;
using Google.Apis.Util;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Fetches the metadata of a set of Gmail messages with batch requests, repeating the ones
/// that fail on a rate limit.
/// </summary>
/// <remarks>
/// A batch answers 200 as a whole and reports the outcome of each request inside it
/// separately, so a message that hit the per-user quota is not seen by the HTTP retry
/// handler. Those are collected and sent again after a pause, with the same back-off as
/// the HTTP retries. Gmail counts every request in a batch against the quota as if it had
/// been sent alone, and recommends batches of at most 50; larger batches are what trips
/// the per-user rate limit in the first place.
///
/// A message that was deleted between the listing and the fetch answers 404 and is skipped
/// quietly. Any other error is reported as a warning for that message, and the rest of the
/// batch still goes through. Only running out of retries fails the whole fetch.
/// </remarks>
internal static class GmailMessageBatch
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType(typeof(GmailMessageBatch));

    /// <summary>
    /// The largest number of requests sent in one batch, as recommended by Gmail.
    /// </summary>
    public const int BatchSize = 50;

    /// <summary>
    /// Fetches the metadata of the given messages.
    /// </summary>
    /// <param name="service">The Gmail service of the user.</param>
    /// <param name="userId">The user whose messages are fetched.</param>
    /// <param name="messageIds">The ids of the messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The messages that could be fetched.</returns>
    public static Task<List<Message>> GetMetadataAsync(GmailService service, string userId, IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken)
        => GetMetadataAsync(service, userId, messageIds, GoogleRetryHandler.CreateBackOff(), cancellationToken);

    /// <summary>
    /// Fetches the metadata of the given messages, pausing by the given policy between retries.
    /// </summary>
    /// <param name="service">The Gmail service of the user.</param>
    /// <param name="userId">The user whose messages are fetched.</param>
    /// <param name="messageIds">The ids of the messages.</param>
    /// <param name="backOff">The policy that decides how long to pause before each retry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The messages that could be fetched.</returns>
    public static Task<List<Message>> GetMetadataAsync(GmailService service, string userId, IReadOnlyCollection<string> messageIds, IBackOff backOff, CancellationToken cancellationToken)
        => GetMetadataAsync(service, userId, messageIds, backOff, (_, _) => { }, cancellationToken);

    /// <summary>
    /// Fetches the metadata of the given messages, pausing by the given policy between retries.
    /// </summary>
    /// <param name="service">The Gmail service of the user.</param>
    /// <param name="userId">The user whose messages are fetched.</param>
    /// <param name="messageIds">The ids of the messages.</param>
    /// <param name="backOff">The policy that decides how long to pause before each retry.</param>
    /// <param name="onFetchFailed">Called for a message that could not be read for a reason other than being deleted, with its id and the error, after the warning is written.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The messages that could be fetched.</returns>
    public static async Task<List<Message>> GetMetadataAsync(GmailService service, string userId, IReadOnlyCollection<string> messageIds, IBackOff backOff, Action<string, RequestError> onFetchFailed, CancellationToken cancellationToken)
    {
        var messages = new List<Message>(messageIds.Count);
        var pending = messageIds.ToList();
        var attempt = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var failed = new List<(string Id, RequestError Error)>();
            foreach (var chunk in pending.Chunk(BatchSize))
            {
                var batch = new BatchRequest(service);
                foreach (var id in chunk)
                {
                    var request = service.Users.Messages.Get(userId, id);
                    request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                    batch.Queue<Message>(request, (message, error, index, response) =>
                    {
                        lock (messages)
                        {
                            if (error == null && message != null)
                                messages.Add(message);
                            else
                                failed.Add((id, error ?? new RequestError { Code = (int)(response?.StatusCode ?? 0), Message = "Empty response" }));
                        }
                    });
                }

                await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            pending.Clear();
            RequestError? lastRetryable = null;
            foreach (var (id, error) in failed)
            {
                if (error.Code == (int)HttpStatusCode.NotFound)
                {
                    Log.WriteVerboseMessage(LOGTAG, "GmailMessageGone", Strings.GmailMessageGone(id, userId));
                    continue;
                }

                if (GoogleServiceErrors.IsRetryable(error))
                {
                    pending.Add(id);
                    lastRetryable = error;
                    continue;
                }

                Log.WriteWarningMessage(LOGTAG, "GmailMessageFetchFailed", null, Strings.GmailMessageFetchFailed(id, userId, error.Code, error.Message ?? ""));
                onFetchFailed(id, error);
            }

            if (pending.Count == 0)
                break;

            attempt++;
            var delay = backOff.GetNextBackOff(attempt);
            if (delay < TimeSpan.Zero || delay > GoogleRetryHandler.MaxWait)
                throw new GoogleApiException(service.Name, Strings.GmailBatchRetriesExhausted(pending.Count, userId, lastRetryable!.Code, lastRetryable.Message ?? ""))
                {
                    HttpStatusCode = (HttpStatusCode)lastRetryable.Code,
                    Error = lastRetryable
                };

            Log.WriteInformationMessage(LOGTAG, "GmailBatchRetry", Strings.GmailBatchRetry(pending.Count, userId, lastRetryable!.Code, attempt, delay));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return messages;
    }
}
