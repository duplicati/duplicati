
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

using System;
using System.Globalization;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

public class TimeparserTests : BasicSetupHelper
{
    [Category("Utility")]
    [TestCase("0s", 0)]
    [TestCase("10s", 10000)]
    [TestCase("1m", 60000)]
    [TestCase("2h", 7200000)]
    [TestCase("1D", 86400000)]
    [TestCase("1W", 604800000)]
    [Category("Utility")]
    public void TestParseTimeSpan_EnglishLocale(string input, long expectedMilliseconds)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var ts = Timeparser.ParseTimeSpan(input);
            Assert.AreEqual(expectedMilliseconds, (long)ts.TotalMilliseconds);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Category("Utility")]
    [TestCase("fr-CA", "0s", 0)]
    [TestCase("fr-CA", "10s", 10000)]
    [TestCase("de-DE", "5m", 300000)]
    [TestCase("en-GB", "2h", 7200000)]
    [TestCase("sv-SE", "1D", 86400000)]
    public void TestParseTimeSpan_LocaleVariations(string locale, string input, long expectedMilliseconds)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(locale);
            var ts = Timeparser.ParseTimeSpan(input);
            Assert.AreEqual(expectedMilliseconds, (long)ts.TotalMilliseconds);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    [Category("Utility")]
    public void TestParseTimeSpan_InvalidInput_Throws()
    {
        Assert.Throws<Exception>(() => Timeparser.ParseTimeSpan("xyz"));
    }

    [Test]
    [Category("Utility")]
    public void TestParseTimeSpan_EmptyString()
    {
        var ts = Timeparser.ParseTimeSpan("");
        Assert.AreEqual(TimeSpan.Zero, ts);
    }

    [Test]
    [Category("Utility")]
    public void TestParseTimeSpan_Now()
    {
        var now = DateTime.Now;
        var parsed = Timeparser.ParseTimeInterval("now", now);
        Assert.That((parsed - now).TotalSeconds, Is.LessThan(1));
    }

    [Test]
    [Category("Utility")]
    public void TestParseTimeInterval_WithOffsetAndNegate()
    {
        var offset = new DateTime(2020, 1, 1);
        var result = Timeparser.ParseTimeInterval("10s", offset, negate: true);
        Assert.AreEqual(offset.AddSeconds(-10), result);
    }

    [Category("Utility")]
    [TestCase("0s", 0)]
    [TestCase("15m", 900000)]
    [TestCase("1h30m", 5400000)]
    public void TestParseTimeInterval_SpecialCases(string input, long expectedMilliseconds)
    {
        var baseTime = new DateTime(2023, 1, 1, 0, 0, 0);
        var result = Timeparser.ParseTimeInterval(input, baseTime);
        var diff = (long)(result - baseTime).TotalMilliseconds;
        Assert.AreEqual(expectedMilliseconds, diff);
    }

    [Category("Utility")]
    [TestCase("0s", "fr-CA")]
    [TestCase("0s", "de-DE")]
    public void TestParseTimeInterval_LocaleSensitiveAmbiguity(string input, string locale)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(locale);
            var baseTime = new DateTime(2023, 1, 1);
            var result = Timeparser.ParseTimeInterval(input, baseTime);
            Assert.AreEqual(baseTime, result); // 0s should not change the time
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Category("Utility")]
    [TestCase("en-US")]
    [TestCase("fr-CA")]
    [TestCase("de-DE")]
    public void DSTAware_AddHour_SpringForward_ShouldSkipMissingHour(string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var start = new DateTime(2024, 3, 10, 6, 30, 0, DateTimeKind.Utc); // UTC 1:30 AM EST
            var result = Timeparser.DSTAwareParseTimeInterval("1h", start, tz);
            var expected = new DateTime(2024, 3, 10, 7, 30, 0, DateTimeKind.Utc); // UTC 3:30 AM EDT
            Assert.AreEqual(expected, result);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Category("Utility")]
    [TestCase("en-US")]
    [TestCase("fr-CA")]
    [TestCase("de-DE")]
    public void DSTAware_AddHour_FallBack_ShouldHandleAmbiguousTime(string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var start = new DateTime(2024, 11, 3, 5, 30, 0, DateTimeKind.Utc); // 1:30 AM EDT
            var result = Timeparser.DSTAwareParseTimeInterval("1h", start, tz);
            var expected = new DateTime(2024, 11, 3, 6, 30, 0, DateTimeKind.Utc); // 1:30 AM EST
            Assert.AreEqual(expected, result);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Category("Utility")]
    [TestCase("en-US")]
    [TestCase("fr-CA")]
    [TestCase("de-DE")]
    public void DSTAware_AddMultipleUnits_Combined(string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var start = new DateTime(2024, 3, 10, 4, 0, 0, DateTimeKind.Utc); // March 9, 11 PM EST
            var result = Timeparser.DSTAwareParseTimeInterval("1D2h", start, tz);
            var expected = new DateTime(2024, 3, 11, 6, 0, 0, DateTimeKind.Utc); // March 11, 1 AM EDT
            Assert.AreEqual(expected, result);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Category("Utility")]
    [TestCase("en-US")]
    [TestCase("fr-CA")]
    [TestCase("de-DE")]
    public void DSTAware_KeepTime_SpringForward_ShouldKeepClockTime(string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var start = new DateTime(2024, 3, 9, 6, 30, 0, DateTimeKind.Utc); // March 9, 1:30 AM EST
            var result = Timeparser.DSTAwareParseTimeInterval("1D", start, tz);
            var expected = new DateTime(2024, 3, 10, 6, 30, 0, DateTimeKind.Utc); // March 10, 1:30 AM EDT
            Assert.AreEqual(expected, result);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Category("Utility")]
    [TestCase("en-US")]
    [TestCase("fr-CA")]
    [TestCase("de-DE")]
    public void DSTAware_KeepTime_FallBack_ShouldKeepClockTime(string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var start = new DateTime(2024, 11, 2, 5, 30, 0, DateTimeKind.Utc); // Nov 2, 1:30 AM EDT
            var result = Timeparser.DSTAwareParseTimeInterval("1D", start, tz);
            var expected = new DateTime(2024, 11, 3, 5, 30, 0, DateTimeKind.Utc); // Nov 3, 1:30 AM EST
            Assert.AreEqual(expected, result);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void SpringForward_Add1Hour_ShouldNeverReturnNonexistentLocalTime()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var rawStart = new DateTime(2024, 3, 10, 6, 30, 0, DateTimeKind.Utc); // 1:30 AM EST (UTC)

        var resultUtc = Timeparser.DSTAwareParseTimeInterval("1h", rawStart, tz);

        // Convert the result back to local time in the given time zone
        var resultLocal = TimeZoneInfo.ConvertTimeFromUtc(resultUtc, tz);

        // Verify the returned local time is not within the invalid DST range
        Assert.IsFalse(tz.IsInvalidTime(resultLocal), $"Returned a non-existent local time: {resultLocal}");
    }

    [Test]
    public void MultipleUnits_SpringForward_ShouldAvoidInvalidLocalTime()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var rawStart = new DateTime(2024, 3, 10, 4, 30, 0, DateTimeKind.Utc); // 11:30 PM March 9 EST (UTC)

        var resultUtc = Timeparser.DSTAwareParseTimeInterval("1D2h", rawStart, tz);

        var resultLocal = TimeZoneInfo.ConvertTimeFromUtc(resultUtc, tz);
        Assert.IsFalse(tz.IsInvalidTime(resultLocal), $"Returned a non-existent local time: {resultLocal}");
    }

    [Test]
    public void AmbiguousTime_FallBack_ShouldReturnValidLocalTime()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var rawStart = new DateTime(2024, 11, 3, 5, 30, 0, DateTimeKind.Utc); // 1:30 AM EDT (UTC)

        var resultUtc = Timeparser.DSTAwareParseTimeInterval("1h", rawStart, tz);
        var resultLocal = TimeZoneInfo.ConvertTimeFromUtc(resultUtc, tz);

        // This will be an ambiguous time but still valid
        Assert.IsTrue(tz.IsAmbiguousTime(resultLocal) || !tz.IsInvalidTime(resultLocal),
            $"Returned an invalid or ambiguous local time incorrectly: {resultLocal}");
    }
}
