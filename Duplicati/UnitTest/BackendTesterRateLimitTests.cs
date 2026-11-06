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

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Duplicati.CommandLine.BackendTester;
using Duplicati.Library.Interface;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The backend tester retries a failed operation after 1 and 2 seconds. A rate limit
    /// counted per minute, such as Google's, is still in force by then, so all the retries
    /// were used up and the test run failed.
    /// </summary>
    [TestFixture]
    [Category("BackendTesterVerification")]
    public class BackendTesterRateLimitTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// A rate limit the way the Google backends report it
        /// </summary>
        private static Exception RateLimited(RetryConditionHeaderValue? retryAfter)
            => new HttpRequestException("403 rateLimitExceeded", new TooManyRequestException(retryAfter!));

        [Test]
        public void OtherFailuresKeepTheShortDelays()
        {
            Assert.That(Program.GetRetryDelay(new HttpRequestException("500"), 1, Now), Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(Program.GetRetryDelay(new HttpRequestException("500"), 2, Now), Is.EqualTo(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public void ARateLimitWithoutRetryAfterWaitsAMinute()
        {
            Assert.That(Program.GetRetryDelay(RateLimited(null), 1, Now), Is.EqualTo(Program.RateLimitDelay));
            Assert.That(Program.RateLimitDelay, Is.EqualTo(TimeSpan.FromMinutes(1)));
        }

        [Test]
        public void ARateLimitInsideAnAggregateExceptionIsFound()
        {
            // The synchronous calls in the tester wrap the failure in an AggregateException
            var ex = new AggregateException(RateLimited(null));

            Assert.That(Program.GetRetryDelay(ex, 1, Now), Is.EqualTo(Program.RateLimitDelay));
        }

        [Test]
        public void ARetryAfterDelayIsFollowed()
        {
            var ex = RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));

            Assert.That(Program.GetRetryDelay(ex, 1, Now), Is.EqualTo(TimeSpan.FromSeconds(30)));
        }

        [Test]
        public void ARetryAfterDateIsFollowed()
        {
            var ex = RateLimited(new RetryConditionHeaderValue(Now.AddSeconds(45)));

            Assert.That(Program.GetRetryDelay(ex, 1, Now), Is.EqualTo(TimeSpan.FromSeconds(45)));
        }

        [Test]
        public void ARetryAfterShorterThanTheBackoffKeepsTheBackoff()
        {
            var ex = RateLimited(new RetryConditionHeaderValue(Now.AddSeconds(-5)));

            Assert.That(Program.GetRetryDelay(ex, 2, Now), Is.EqualTo(TimeSpan.FromSeconds(2)));
        }
    }
}
