// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library
{
    /// <summary>
    /// Helper class to manage the Retry-After header for a given URL.
    /// </summary>
    public class RetryAfterHelper
    {
        /// <summary>
        /// The log tag for this class.
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType<RetryAfterHelper>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryAfterHelper"/> class.
        /// </summary>
        private RetryAfterHelper()
        {
        }

        // Whenever a response includes a Retry-After header, we'll update this timestamp with when we can next
        // send a request. And before sending any requests, we'll make sure to wait until at least this time.
        // Since this may be read and written by multiple threads, it is stored as a long and updated using Interlocked.Exchange.
        private long retryAfter = DateTimeOffset.MinValue.UtcTicks;

        /// <summary>
        /// The lock object to ensure thread safety for accessing the retry after helpers.
        /// </summary>
        private static object _lock = new object();
        /// <summary>
        /// Lookup table that maps the URL to the RetryAfterHelper for that URL.
        /// </summary>
        private static Dictionary<string, RetryAfterHelper> _retryAfterHelpers = new Dictionary<string, RetryAfterHelper>();

        /// <summary>
        /// Backends generally do not keep any state, but the retry after header is stateful,
        /// as it depends on the last request. This method obtains a helper object for the url,
        /// so a small amount of state can be shared between instances.
        /// </summary>
        /// <param name="url">The URL to get the RetryAfterHelper for.</param>
        /// <returns>The RetryAfterHelper for the given URL.</returns>
        public static RetryAfterHelper CreateOrGetRetryAfterHelper(string url)
        {
            lock (_lock)
            {
                // Remove any expired entries
                foreach (var (k, h) in _retryAfterHelpers.ToArray())
                    if (h.retryAfter < DateTimeOffset.UtcNow.UtcTicks)
                        _retryAfterHelpers.Remove(k);

                // Get the RetryAfterHelper for the given URL, or create a new one if it doesn't exist
                if (!_retryAfterHelpers.TryGetValue(url, out var retryAfterHelper))
                    _retryAfterHelpers[url] = retryAfterHelper = new RetryAfterHelper();

                return retryAfterHelper;
            }
        }

        /// <summary>
        /// Sets the Retry-After header value for the next request.
        /// </summary>
        /// <param name="retryAfter">The Retry-After header value to set.</param>
        public void SetRetryAfter(RetryConditionHeaderValue retryAfter)
        {
            if (retryAfter != null)
            {
                DateTimeOffset? delayUntil = null;
                if (retryAfter.Delta.HasValue)
                {
                    delayUntil = DateTimeOffset.UtcNow + retryAfter.Delta.Value;
                }
                else if (retryAfter.Date.HasValue)
                {
                    delayUntil = retryAfter.Date.Value;
                }

                if (delayUntil.HasValue)
                {
                    // Set the retry timestamp to the UTC version of the timestamp.
                    long newRetryAfter = delayUntil.Value.UtcTicks;

                    // Update the persisted retry after timestamp
                    long replacedRetryAfter;
                    long currentRetryAfter;
                    do
                    {
                        currentRetryAfter = Interlocked.Read(ref this.retryAfter);

                        if (newRetryAfter < currentRetryAfter)
                        {
                            // If the current retry after is already past the new value, then no need to update it again.
                            break;
                        }
                        else
                        {
                            replacedRetryAfter = Interlocked.CompareExchange(ref this.retryAfter, newRetryAfter, currentRetryAfter);
                        }
                    }
                    while (replacedRetryAfter != currentRetryAfter);
                }
            }
        }

        /// <summary>
        /// Waits for the time specified in the Retry-After header before returning.
        /// </summary>
        public void WaitForRetryAfter()
        {
            this.WaitForRetryAfterAsync(CancellationToken.None).Await();
        }

        /// <summary>
        /// Waits for the time specified in the Retry-After header before continuing.
        /// </summary>
        /// <param name="cancelToken">The cancellation token to cancel the wait.</param>
        /// <returns>A task that completes when the wait is done.</returns>
        public async Task WaitForRetryAfterAsync(CancellationToken cancelToken)
        {
            TimeSpan delay = this.GetDelayTime();

            if (delay > TimeSpan.Zero)
            {
                Log.WriteInformationMessage(
                    LOGTAG,
                    "RetryAfterWait",
                    "Waiting for {0} to respect Retry-After header",
                    delay);

                await Task.Delay(delay, cancelToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gets the delay time until the next request can be made.
        /// </summary>
        /// <returns>The delay time until the next request can be made.</returns>
        private TimeSpan GetDelayTime()
        {
            // Make sure this is thread safe in case multiple calls are made concurrently to this backend
            // This is done by reading the value into a local value which is then parsed and operated on locally.
            long retryAfterTicks = Interlocked.Read(ref this.retryAfter);
            DateTimeOffset delayUntil = new DateTimeOffset(retryAfterTicks, TimeSpan.Zero);

            TimeSpan delay;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            // Make sure delayUntil is in the future and delay until then if so
            if (delayUntil >= now)
            {
                delay = delayUntil - now;
            }
            else
            {
                // If the date given was in the past then don't wait at all
                delay = TimeSpan.Zero;
            }

            return delay;
        }
    }
}
