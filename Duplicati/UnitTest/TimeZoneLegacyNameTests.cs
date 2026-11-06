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
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Legacy time zone names such as CET or EST are only in tzdata-legacy on
    /// Ubuntu 26.04 and Debian 13. Without it, CET was not found, and EST and MST
    /// matched the display names of unrelated zones ("Creston", "Amsterdam").
    /// </summary>
    [TestFixture]
    public class TimeZoneLegacyNameTests
    {
        private static readonly DateTime Winter = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Summer = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        [Category("TimeZoneHelper")]
        [TestCase("CET", 1, 2)]
        [TestCase("MET", 1, 2)]
        [TestCase("EET", 2, 3)]
        [TestCase("WET", 0, 1)]
        [TestCase("EST", -5, -5)]
        [TestCase("MST", -7, -7)]
        [TestCase("HST", -10, -10)]
        [TestCase("EST5EDT", -5, -4)]
        [TestCase("CST6CDT", -6, -5)]
        [TestCase("MST7MDT", -7, -6)]
        [TestCase("PST8PDT", -8, -7)]
        [TestCase("US/Eastern", -5, -4)]
        [TestCase("Japan", 9, 9)]
        public void LegacyNameResolvesToItsZone(string name, int winterHours, int summerHours)
        {
            foreach (var timeZone in new[] { TimeZoneHelper.GetTimeZoneById(name), TimeZoneHelper.FindTimeZone(name) })
            {
                Assert.That(timeZone, Is.Not.Null, name);
                Assert.That(timeZone!.GetUtcOffset(Winter), Is.EqualTo(TimeSpan.FromHours(winterHours)), $"{name} resolved to {timeZone.Id} in winter");
                Assert.That(timeZone.GetUtcOffset(Summer), Is.EqualTo(TimeSpan.FromHours(summerHours)), $"{name} resolved to {timeZone.Id} in summer");
            }
        }

        [Test]
        [Category("TimeZoneHelper")]
        public void FindTimeZoneMatchesWholeWordsOfTheDisplayName()
        {
            // The display names depend on the language of the system, so take a word from one
            var word = TimeZoneInfo.GetSystemTimeZones()
                .SelectMany(tz => Regex.Matches(tz.DisplayName, @"\p{L}{6,}").Select(m => m.Value))
                .First();
            Assert.That(TimeZoneHelper.FindTimeZone(word), Is.Not.Null, word);

            // Part of a word, as "EST" is part of "Creston"
            Assert.That(TimeZoneHelper.FindTimeZone("rlin"), Is.Null);
        }
    }
}
