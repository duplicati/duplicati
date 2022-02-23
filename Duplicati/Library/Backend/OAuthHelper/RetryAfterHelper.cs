//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library
{
    public class RetryAfterHelper
    {
        private static readonly string LOGTAG = Log.LogTagFromType<RetryAfterHelper>();

        // Whenever a response includes a Retry-After header, we'll update this timestamp with when we can next
        // send a request. And before sending any requests, we'll make sure to wait until at least this time.
        // Since this may be read and written by multiple threads, it is stored as a long and updated using Interlocked.Exchange.
        private long retryAfter = DateTimeOffset.MinValue.UtcTicks;

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

        public void WaitForRetryAfter()
        {
            this.WaitForRetryAfterAsync(CancellationToken.None).Await();
        }

        public async Task WaitForRetryAfterAsync(CancellationToken cancelToken)
        {
            TimeSpan delay = this.GetDelayTime();

            if (delay > TimeSpan.Zero)
            {
                Log.WriteProfilingMessage(
                    LOGTAG,
                    "RetryAfterWait",
                    "Waiting for {0} to respect Retry-After header",
                    delay);

                await Task.Delay(delay).ConfigureAwait(false);
            }
        }

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
