// Copyright (C) 2026, The Duplicati Team
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
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Slows down brute-force attacks by delaying password verification after failed attempts.
/// The server has a single password, so the throttle is global rather than per-client;
/// a per-client throttle is trivially bypassed with multiple source addresses,
/// and cannot distinguish clients behind a reverse proxy.
/// </summary>
/// <param name="baseDelay">The delay imposed after the first failed attempt; doubled for each consecutive failure.</param>
/// <param name="maxDelay">The upper limit for the delay.</param>
/// <param name="failureResetPeriod">The period without failed attempts after which the failure count is reset.</param>
/// <param name="maxPendingAttempts">The maximum number of attempts allowed to wait for verification.</param>
public class LoginAttemptThrottle(TimeSpan baseDelay, TimeSpan maxDelay, TimeSpan failureResetPeriod, int maxPendingAttempts) : ILoginAttemptThrottle
{
    private static readonly string LOGTAG = Log.LogTagFromType<LoginAttemptThrottle>();

    /// <summary>
    /// The default delay after the first failed attempt
    /// </summary>
    private static readonly TimeSpan DEFAULT_BASE_DELAY = TimeSpan.FromMilliseconds(250);
    /// <summary>
    /// The default maximum delay between attempts
    /// </summary>
    private static readonly TimeSpan DEFAULT_MAX_DELAY = TimeSpan.FromSeconds(10);
    /// <summary>
    /// The default period without failures that resets the failure count
    /// </summary>
    private static readonly TimeSpan DEFAULT_FAILURE_RESET_PERIOD = TimeSpan.FromMinutes(15);
    /// <summary>
    /// The default maximum number of attempts waiting for verification
    /// </summary>
    private const int DEFAULT_MAX_PENDING_ATTEMPTS = 10;

    /// <summary>
    /// The lock that serializes verification attempts
    /// </summary>
    private readonly SemaphoreSlim m_lock = new(1, 1);
    /// <summary>
    /// The number of attempts waiting for or performing verification
    /// </summary>
    private int m_pending;
    /// <summary>
    /// The number of consecutive failed attempts; guarded by <see cref="m_lock"/>
    /// </summary>
    private int m_failures;
    /// <summary>
    /// The time of the most recent failed attempt; guarded by <see cref="m_lock"/>
    /// </summary>
    private DateTime m_lastFailure;
    /// <summary>
    /// The earliest time the next attempt may be verified; guarded by <see cref="m_lock"/>
    /// </summary>
    private DateTime m_nextAttempt;

    /// <summary>
    /// Creates a new throttle with the default settings
    /// </summary>
    public LoginAttemptThrottle()
        : this(DEFAULT_BASE_DELAY, DEFAULT_MAX_DELAY, DEFAULT_FAILURE_RESET_PERIOD, DEFAULT_MAX_PENDING_ATTEMPTS)
    {
    }

    /// <inheritdoc/>
    public async Task<bool> VerifyAsync(Func<bool> verify, CancellationToken ct)
    {
        // Bound the queue, so an attack cannot tie up an unlimited number of requests
        if (Interlocked.Increment(ref m_pending) > maxPendingAttempts)
        {
            Interlocked.Decrement(ref m_pending);
            throw new TooManyRequestsException("Too many login attempts, try again later");
        }

        try
        {
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                if (m_failures > 0 && now - m_lastFailure > failureResetPeriod)
                    m_failures = 0;

                var wait = m_nextAttempt - now;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct).ConfigureAwait(false);

                if (verify())
                {
                    m_failures = 0;
                    m_nextAttempt = default;
                    return true;
                }

                m_failures++;
                m_lastFailure = DateTime.UtcNow;
                var delay = GetDelay(m_failures);
                m_nextAttempt = m_lastFailure + delay;
                Log.WriteWarningMessage(LOGTAG, "FailedLoginAttempt", null, "Failed login attempt #{0}, delaying next attempt by {1}", m_failures, delay);
                return false;
            }
            finally
            {
                m_lock.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref m_pending);
        }
    }

    /// <summary>
    /// Calculates the delay to impose after a number of consecutive failures
    /// </summary>
    /// <param name="failures">The number of consecutive failures</param>
    /// <returns>The delay before the next attempt</returns>
    private TimeSpan GetDelay(int failures)
    {
        // Cap the exponent to avoid overflow
        var factor = 1L << Math.Min(failures - 1, 30);
        var ticks = baseDelay.Ticks * (double)factor;
        return ticks >= maxDelay.Ticks ? maxDelay : TimeSpan.FromTicks((long)ticks);
    }
}
