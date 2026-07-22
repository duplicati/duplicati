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

#nullable enable

using System;
using Duplicati.Server;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the scheduler's next-run computation. The scheduler caches the
    /// computed next run per schedule and only recomputes when the schedule
    /// fingerprint changes, so the fingerprint must change for every field that
    /// affects the next run time — otherwise editing that field has no effect
    /// until the server restarts (issue #2487).
    /// </summary>
    [TestFixture]
    [Category("Scheduler")]
    public class SchedulerTests
    {
        /// <summary>
        /// A minimal schedule holder for fingerprint tests.
        /// </summary>
        private sealed class FakeSchedule : ISchedule
        {
            public long ID { get; set; }
            public string[] Tags { get; set; } = [];
            public DateTime Time { get; set; }
            public string Repeat { get; set; } = "1D";
            public DateTime LastRun { get; set; }
            public string Rule { get; set; } = "";
            public DayOfWeek[] AllowedDays { get; set; } = [];
        }

        private static FakeSchedule MakeSchedule()
            => new FakeSchedule
            {
                Time = new DateTime(2030, 1, 1, 13, 0, 0, DateTimeKind.Utc),
                Repeat = "1D",
                AllowedDays = [DayOfWeek.Friday],
            };

        [Test]
        public void FingerprintIsStableForEqualSchedules()
        {
            Assert.AreEqual(
                Scheduler.GetScheduleFingerprint(MakeSchedule()),
                Scheduler.GetScheduleFingerprint(MakeSchedule()));
        }

        [Test]
        public void FingerprintIgnoresDayOrderAndDuplicates()
        {
            var a = MakeSchedule();
            a.AllowedDays = [DayOfWeek.Monday, DayOfWeek.Friday];
            var b = MakeSchedule();
            b.AllowedDays = [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Friday];

            Assert.AreEqual(Scheduler.GetScheduleFingerprint(a), Scheduler.GetScheduleFingerprint(b));
        }

        [Test]
        public void FingerprintChangesWhenDaysChange()
        {
            var a = MakeSchedule();
            var b = MakeSchedule();
            b.AllowedDays = [DayOfWeek.Friday, DayOfWeek.Wednesday];

            Assert.AreNotEqual(Scheduler.GetScheduleFingerprint(a), Scheduler.GetScheduleFingerprint(b),
                "Adding an allowed day must invalidate the cached next run (issue #2487)");
        }

        [Test]
        public void FingerprintChangesWhenRepeatChanges()
        {
            var a = MakeSchedule();
            var b = MakeSchedule();
            b.Repeat = "1W";

            Assert.AreNotEqual(Scheduler.GetScheduleFingerprint(a), Scheduler.GetScheduleFingerprint(b),
                "Changing the repetition interval must invalidate the cached next run");
        }

        [Test]
        public void FingerprintChangesWhenTimeChanges()
        {
            var a = MakeSchedule();
            var b = MakeSchedule();
            b.Time = b.Time.AddMinutes(30);

            Assert.AreNotEqual(Scheduler.GetScheduleFingerprint(a), Scheduler.GetScheduleFingerprint(b));
        }

        [Test]
        public void FingerprintTreatsNullAndEmptyDaysTheSame()
        {
            var a = MakeSchedule();
            a.AllowedDays = null!;
            var b = MakeSchedule();
            b.AllowedDays = [];

            Assert.AreEqual(Scheduler.GetScheduleFingerprint(a), Scheduler.GetScheduleFingerprint(b));
        }

        /// <summary>
        /// Documents why the cache must be invalidated when the allowed days change:
        /// <see cref="Scheduler.GetNextValidTime"/> can only move a time forward, so a
        /// cached next-run computed under the old day set can never move to an earlier
        /// (newly allowed) day. Only recomputing from the schedule's base time can.
        /// </summary>
        [Test]
        public void CachedNextRunCannotMoveEarlierWhenDaysAreAdded()
        {
            var tz = TimeZoneInfo.Utc;

            // A daily schedule, base time Tuesday 2030-01-01 13:00, allowed: Friday only.
            var baseTime = new DateTime(2030, 1, 1, 13, 0, 0, DateTimeKind.Utc);
            var lastRun = new DateTime(2029, 12, 25, 13, 0, 0, DateTimeKind.Utc);

            var nextUnderFridayOnly = Scheduler.GetNextValidTime(baseTime, lastRun, "1D", [DayOfWeek.Friday], tz);
            Assert.AreEqual(new DateTime(2030, 1, 4, 13, 0, 0, DateTimeKind.Utc), nextUnderFridayOnly,
                "Sanity: the first allowed run is Friday 2030-01-04");

            // The user now ALSO allows Wednesday. The correct next run is Wednesday
            // 2030-01-02, as a fresh computation from the base time shows:
            var freshRecompute = Scheduler.GetNextValidTime(baseTime, lastRun, "1D", [DayOfWeek.Wednesday, DayOfWeek.Friday], tz);
            Assert.AreEqual(new DateTime(2030, 1, 2, 13, 0, 0, DateTimeKind.Utc), freshRecompute,
                "A recomputation from the base time picks up the newly allowed earlier day");

            // But feeding the CACHED next run (Friday) back in, as the scheduler does on
            // a cache hit, cannot move backwards to Wednesday:
            var fromStaleCache = Scheduler.GetNextValidTime(nextUnderFridayOnly, lastRun, "1D", [DayOfWeek.Wednesday, DayOfWeek.Friday], tz);
            Assert.AreEqual(nextUnderFridayOnly, fromStaleCache,
                "The forward-only fix-up keeps the stale Friday, which is why the cache must be invalidated");
        }
    }
}
