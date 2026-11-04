// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Google.Apis.Http;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Bounds how long one attempt of a request may wait for its response headers, in place of
/// the client's own timeout, which bounds the whole request including every retry.
/// </summary>
/// <remarks>
/// The client library runs its retries inside <see cref="HttpClient.SendAsync(HttpRequestMessage)"/>,
/// and the pauses of <see cref="GoogleRetryHandler"/> add up to minutes, so a request that
/// is rate limited a few times in a row is killed by the client's default timeout of 100
/// seconds before the retry policy is through with it. Worse, the client reports that as a
/// <see cref="TaskCanceledException"/>, which the change tracker and the backup engine
/// take for the user cancelling: the failure is not recorded, and a listing that times out
/// ends the enumeration instead of producing a warning.
///
/// So the client's timeout is switched off by <see cref="Install"/>, and this handler sits
/// below the retry handler, where it times one attempt at a time. An attempt that produces
/// no headers within the timeout fails with an <see cref="HttpRequestException"/> carrying a
/// <see cref="TimeoutException"/>, never with a cancellation, so the retry handler repeats
/// it like any other connection failure and the callers never mistake it for an abort.
/// The timeout covers only the headers, as the client's own would: the body is read by
/// <see cref="Duplicati.Library.Utility.ResumableHttpReadStream"/>, which has a stall
/// timeout of its own.
/// </remarks>
internal sealed class PerAttemptTimeoutHandler : DelegatingHandler
{
    /// <summary>
    /// How long one attempt may wait for its response headers
    /// </summary>
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Creates a handler that times each attempt sent through the inner handler
    /// </summary>
    /// <param name="innerHandler">The handler that sends the request</param>
    /// <param name="timeout">How long one attempt may wait for its response headers, or <see cref="Timeout.InfiniteTimeSpan"/> to wait forever</param>
    public PerAttemptTimeoutHandler(HttpMessageHandler innerHandler, TimeSpan timeout)
        : base(innerHandler)
    {
        _timeout = timeout;
    }

    /// <summary>
    /// How long one attempt may wait for its response headers
    /// </summary>
    public TimeSpan AttemptTimeout => _timeout;

    /// <summary>
    /// Installs the per-attempt timeout on a service client: switches off the client's own
    /// timeout and places this handler below the client's message handler, where the
    /// retries are decided, so each attempt is timed on its own
    /// </summary>
    /// <param name="httpClient">The client built for a Google service</param>
    /// <param name="timeout">How long one attempt may wait for its response headers</param>
    public static void Install(ConfigurableHttpClient httpClient, TimeSpan timeout)
    {
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        var inner = httpClient.MessageHandler.InnerHandler
            ?? throw new InvalidOperationException("The service client has no handler to send its requests through");
        httpClient.MessageHandler.InnerHandler = new PerAttemptTimeoutHandler(inner, timeout);
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_timeout <= TimeSpan.Zero || _timeout == Timeout.InfiniteTimeSpan)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);
        try
        {
            return await base.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The attempt's clock ran out, not the caller's: report it as the connection
            // failure it is, so it is retried and never taken for an abort
            throw new HttpRequestException(
                $"The request {request.RequestUri?.AbsolutePath} received no response within {_timeout}",
                new TimeoutException($"No response within {_timeout}", ex));
        }
    }
}
