// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net;
using Duplicati.Library.Logging;
using Google.Apis.Http;
using Google.Apis.Util;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Retries Google API requests that failed for a reason that passes on its own: a rate
/// limit, a 429, or a server error. Installed on every service client built by
/// <see cref="APIHelper"/>.
/// </summary>
/// <remarks>
/// The stock <see cref="BackOffHandler"/> decides from the status code alone, which cannot
/// recognize the Gmail per-user quota: Gmail reports it as 403 Forbidden, the same code it
/// uses for a missing permission, and the two are told apart only by the error body. This
/// handler reads the body before deciding, see <see cref="GoogleServiceErrors.IsRetryable(HttpStatusCode, IEnumerable{string}, string?)"/>.
///
/// The stock handler also gives up after a wait of 16 seconds, and the message handler it
/// sits in allows three attempts by default. A per-minute quota needs a longer pause than
/// that, so the limits are raised to <see cref="MaxWait"/> and <see cref="NumTries"/>.
/// With the exponential policy from <see cref="CreateBackOff"/> the pauses run 1, 2, 4, ...
/// seconds, and a request that keeps failing is given up after roughly eight minutes.
/// </remarks>
internal class GoogleRetryHandler : BackOffHandler
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<GoogleRetryHandler>();

    /// <summary>
    /// The longest single pause before a retry. A pause longer than this ends the retries.
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The number of attempts a request is allowed, including the first one. The message
    /// handler caps this at 20.
    /// </summary>
    public const int NumTries = 10;

    /// <summary>
    /// Creates the back-off policy shared by the HTTP retries and the batch retries, so that
    /// both wait the same way.
    /// </summary>
    /// <returns>A fresh exponential back-off.</returns>
    public static IBackOff CreateBackOff()
        => new ExponentialBackOff(TimeSpan.FromMilliseconds(250), NumTries);

    /// <summary>
    /// Creates a handler with the default back-off policy.
    /// </summary>
    /// <returns>The handler.</returns>
    public static GoogleRetryHandler CreateDefault()
        => new GoogleRetryHandler(CreateBackOff());

    /// <summary>
    /// Installs retries on a service client: raises the attempt limit and registers this
    /// handler for both failed responses and transport exceptions.
    /// </summary>
    /// <param name="httpClient">The client built for a Google service.</param>
    public static void Install(ConfigurableHttpClient httpClient)
    {
        var handler = CreateDefault();
        httpClient.MessageHandler.NumTries = NumTries;
        httpClient.MessageHandler.AddUnsuccessfulResponseHandler(handler);
        httpClient.MessageHandler.AddExceptionHandler(handler);
    }

    /// <summary>
    /// Creates a handler with the given back-off policy.
    /// </summary>
    /// <param name="backOff">The policy that decides how long to pause before each retry.</param>
    public GoogleRetryHandler(IBackOff backOff)
        : base(new Initializer(backOff) { MaxTimeSpan = MaxWait })
    {
    }

    /// <inheritdoc/>
    public override async Task<bool> HandleResponseAsync(HandleUnsuccessfulResponseArgs args)
    {
        if (!args.SupportsRetry)
            return false;

        if (!await IsRetryableResponseAsync(args.Response, args.CancellationToken).ConfigureAwait(false))
            return false;

        var delay = BackOff.GetNextBackOff(args.CurrentFailedTry);
        if (delay < TimeSpan.Zero || delay > MaxTimeSpan)
            return false;

        Log.WriteInformationMessage(LOGTAG, "GoogleApiRetry", Strings.GoogleApiRetry(args.Response.RequestMessage?.RequestUri?.AbsolutePath ?? "", (int)args.Response.StatusCode, args.CurrentFailedTry, delay));
        await Wait(delay, args.CancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Decides whether a failed response should be retried, reading the body when the status
    /// code alone is ambiguous.
    /// </summary>
    /// <param name="response">The failed response.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the request should be retried; otherwise <c>false</c>.</returns>
    public static async Task<bool> IsRetryableResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var statusCode = response.StatusCode;
        if (statusCode != HttpStatusCode.Forbidden && statusCode != HttpStatusCode.TooManyRequests && (int)statusCode < 500)
            return false;

        string? body = null;
        if (response.Content != null)
        {
            try
            {
                // Buffered, so the client library can still read the body for its own error report
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Decide from the status code alone
            }
        }

        var (reasons, message) = GoogleServiceErrors.ParseErrorBody(body);
        return GoogleServiceErrors.IsRetryable(statusCode, reasons, message);
    }
}
