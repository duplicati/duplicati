using System.Collections.Concurrent;
using System.Net;

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// Limits the requests sent to each mailbox. Graph throttles the Outlook service
/// per app and per mailbox: at most four requests may be in flight for one mailbox
/// (the <c>MailboxConcurrency</c> limit), and once a mailbox answers with HTTP 429
/// every request for it is refused until the Retry-After period is over. The gate
/// keeps the number of concurrent requests under the limit, and makes all requests
/// for a throttled mailbox wait for the same deadline, instead of each request
/// finding out on its own and spending its retries on it.
/// </summary>
internal sealed class MailboxRequestGate
{
    /// <summary>
    /// The number of requests that may be in flight for one mailbox
    /// </summary>
    internal const int MAX_CONCURRENT_REQUESTS = 4;

    /// <summary>
    /// The path segments that follow the owner of a mailbox in a request for the Outlook service
    /// </summary>
    private static readonly HashSet<string> MailboxSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "messages", "mailFolders", "contacts", "contactFolders", "events", "calendar", "calendars",
        "calendarGroups", "calendarView", "outlook", "mailboxSettings", "inferenceClassification",
        "conversations", "threads"
    };

    /// <summary>
    /// The state kept for a single mailbox
    /// </summary>
    private sealed class Mailbox
    {
        /// <summary>
        /// The slots for the requests in flight
        /// </summary>
        public readonly SemaphoreSlim Slots = new(MAX_CONCURRENT_REQUESTS, MAX_CONCURRENT_REQUESTS);
        /// <summary>
        /// The time, in UTC ticks, before which no request may be sent
        /// </summary>
        public long BlockedUntilTicks;
    }

    /// <summary>
    /// A slot for a request in flight, which is handed back when disposed
    /// </summary>
    internal sealed class Slot : IDisposable
    {
        /// <summary>
        /// The semaphore the slot was taken from, or <c>null</c> once handed back
        /// </summary>
        private SemaphoreSlim? _slots;

        /// <summary>
        /// Creates a new slot
        /// </summary>
        /// <param name="slots">The semaphore the slot was taken from</param>
        public Slot(SemaphoreSlim slots) => _slots = slots;

        /// <inheritdoc />
        public void Dispose() => Interlocked.Exchange(ref _slots, null)?.Release();
    }

    /// <summary>
    /// The mailboxes seen so far. The key is the owner as it appears in the request,
    /// which is either a GUID or a user principal name, neither of which is case-sensitive.
    /// </summary>
    private readonly ConcurrentDictionary<string, Mailbox> _mailboxes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the mailbox a request is for.
    /// </summary>
    /// <param name="requestUri">The request URI</param>
    /// <returns>The key of the mailbox, or <c>null</c> if the request is not for a mailbox</returns>
    internal static string? TryGetMailboxKey(Uri? requestUri)
    {
        if (requestUri == null || !requestUri.IsAbsoluteUri)
            return null;

        // The segments are "/", "v1.0/", "users/", "{id}/", "messages/", ...
        var segments = requestUri.Segments;
        for (var i = 1; i < segments.Length - 2; i++)
        {
            var kind = segments[i].TrimEnd('/');
            if (!kind.Equals("users", StringComparison.OrdinalIgnoreCase) && !kind.Equals("groups", StringComparison.OrdinalIgnoreCase))
                continue;

            var owner = WebUtility.UrlDecode(segments[i + 1].TrimEnd('/'));
            var resource = segments[i + 2].TrimEnd('/');
            // Function-style addressing, as in mailFolders('inbox')
            var paren = resource.IndexOf('(');
            if (paren >= 0)
                resource = resource[..paren];

            return owner.Length > 0 && MailboxSegments.Contains(resource)
                ? $"{kind.ToLowerInvariant()}/{owner}"
                : null;
        }

        return null;
    }

    /// <summary>
    /// Waits until a request may be sent to the mailbox, and takes a slot for it.
    /// </summary>
    /// <param name="mailboxKey">The key of the mailbox</param>
    /// <param name="ct">The cancellation token</param>
    /// <returns>The slot, which must be disposed when the request has completed</returns>
    public async Task<Slot> EnterAsync(string mailboxKey, CancellationToken ct)
    {
        var mailbox = _mailboxes.GetOrAdd(mailboxKey, _ => new Mailbox());

        await mailbox.Slots.WaitAsync(ct).ConfigureAwait(false);
        var slot = new Slot(mailbox.Slots);
        try
        {
            // The wait is done while holding the slot, so the requests that were held
            // back are released at the same pace as otherwise, and not all at once.
            // The deadline is read again after each wait, as it may have been moved.
            while (true)
            {
                var remaining = new TimeSpan(Interlocked.Read(ref mailbox.BlockedUntilTicks) - DateTime.UtcNow.Ticks);
                if (remaining <= TimeSpan.Zero)
                    return slot;

                await Task.Delay(remaining, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            slot.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Records that the mailbox throttled a request, which holds back all requests for it.
    /// </summary>
    /// <param name="mailboxKey">The key of the mailbox</param>
    /// <param name="delay">The time to wait before sending another request</param>
    public void ReportThrottled(string mailboxKey, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
            return;

        var mailbox = _mailboxes.GetOrAdd(mailboxKey, _ => new Mailbox());
        var until = DateTime.UtcNow.Ticks + delay.Ticks;

        // Only ever move the deadline forward
        long current;
        while ((current = Interlocked.Read(ref mailbox.BlockedUntilTicks)) < until)
            if (Interlocked.CompareExchange(ref mailbox.BlockedUntilTicks, until, current) == current)
                break;
    }

    /// <summary>
    /// Makes a response hold on to a slot until the response is disposed, as the
    /// request is in flight for as long as its body is being read.
    /// </summary>
    /// <param name="response">The response</param>
    /// <param name="slot">The slot to hand back with the response</param>
    public static void Attach(HttpResponseMessage response, Slot slot)
        => response.Content = new SlotHoldingContent(response.Content, slot);

    /// <summary>
    /// Content that passes everything on to the content it wraps, and hands back a slot when disposed
    /// </summary>
    private sealed class SlotHoldingContent : HttpContent
    {
        /// <summary>
        /// The wrapped content
        /// </summary>
        private readonly HttpContent _inner;
        /// <summary>
        /// The slot to hand back
        /// </summary>
        private readonly Slot _slot;

        /// <summary>
        /// Creates a new instance
        /// </summary>
        /// <param name="inner">The content to wrap</param>
        /// <param name="slot">The slot to hand back when disposed</param>
        public SlotHoldingContent(HttpContent inner, Slot slot)
        {
            _inner = inner;
            _slot = slot;
            foreach (var header in inner.Headers)
                Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        /// <inheritdoc />
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => _inner.CopyToAsync(stream, context);

        /// <inheritdoc />
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => _inner.CopyToAsync(stream, context, cancellationToken);

        /// <inheritdoc />
        protected override Task<Stream> CreateContentReadStreamAsync()
            => _inner.ReadAsStreamAsync();

        /// <inheritdoc />
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => _inner.ReadAsStreamAsync(cancellationToken);

        /// <inheritdoc />
        protected override bool TryComputeLength(out long length)
        {
            length = _inner.Headers.ContentLength ?? -1;
            return length >= 0;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _slot.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
