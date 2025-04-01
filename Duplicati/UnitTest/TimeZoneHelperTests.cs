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
using System.Linq;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

public class TimeZoneHelperTests : BasicSetupHelper
{
    private static string CETName = OperatingSystem.IsWindows() 
        // For some reason CET is not recognized on Windows
        // It has no effect outside the tests, as the string is not
        // present outside the tests
        ? "Central European Standard Time"
        : "CET";
    
    [Test]
    [Category("TimeZoneHelper")]
    public void GetTimeZoneFromAbbreviationTest()
    {
        var timeZone = TimeZoneHelper.FindTimeZone("EST");
        Assert.NotNull(timeZone);

        timeZone = TimeZoneHelper.FindTimeZone("Pacific Standard Time");
        Assert.NotNull(timeZone);

        timeZone = TimeZoneHelper.FindTimeZone(CETName);
        Assert.NotNull(timeZone);
    }

    [Test]
    [Category("TimeZoneHelper")]
    public void GetTimeZonesTest()
    {
        var timeZones = TimeZoneHelper.GetTimeZones();
        Assert.That(timeZones.Any(x => x.CurrentUtcOffset.Hours == -5));
        Assert.That(timeZones.Any(x => x.CurrentUtcOffset.Hours == -8));
        Assert.That(timeZones.Any(x => x.CurrentUtcOffset.Hours == 1));
        Assert.That(timeZones.Any(x => x.CurrentUtcOffset.Hours == 2));
    }

    [Test]
    [Category("TimeZoneHelper")]
    public void CheckTestSystemTimeZonesAreCorrect()
    {
        var timeZone = TimeZoneHelper.FindTimeZone(CETName);
        var localTime = new DateTimeOffset(2024, 3, 31, 1, 59, 59, timeZone.BaseUtcOffset);

        // Check that the system time is correct
        Assert.AreEqual(1, timeZone.GetUtcOffset(localTime).Hours);
        Assert.AreEqual(2, timeZone.GetUtcOffset(localTime.AddSeconds(2)).Hours);
    }

    [Test]
    [Category("TimeZoneHelper")]
    public void CheckAddIsStableOverDSTForward()
    {
        var timeZone = TimeZoneHelper.FindTimeZone(CETName);
        var localTime = new DateTimeOffset(2024, 3, 31, 1, 59, 59, timeZone.BaseUtcOffset);

        var utcTime = localTime.ToUniversalTime().DateTime;

        var adjustedTimeSeconds = timeZone.DSTAwareAddSeconds(utcTime, 2);
        var adjustedTimeHours = timeZone.DSTAwareAddHours(utcTime, 1);
        var adjustedTimeDays = timeZone.DSTAwareAddDays(utcTime, 1);
        Assert.AreEqual(utcTime.AddHours(-1).AddSeconds(2), adjustedTimeSeconds);
        Assert.AreEqual(utcTime.AddHours(-1).AddHours(1), adjustedTimeHours);
        Assert.AreEqual(utcTime.AddHours(-1).AddDays(1), adjustedTimeDays);
    }

    [Test]
    [Category("TimeZoneHelper")]
    public void CheckAddIsStableOverDSTBackward()
    {
        var timeZone = TimeZoneHelper.FindTimeZone(CETName);
        var dstOffset = timeZone.GetUtcOffset(new DateTimeOffset(2024, 10, 26, 14, 0, 0, timeZone.BaseUtcOffset));
        var localTime = new DateTimeOffset(2024, 10, 27, 2, 59, 59, dstOffset);

        var utcTime = localTime.ToUniversalTime().DateTime;

        var adjustedTimeSeconds = timeZone.DSTAwareAddSeconds(utcTime, 2);
        var adjustedTimeHours = timeZone.DSTAwareAddHours(utcTime, 1);
        var adjustedTimeDays = timeZone.DSTAwareAddDays(utcTime, 1);
        Assert.AreEqual(utcTime.AddHours(1).AddSeconds(2), adjustedTimeSeconds);
        Assert.AreEqual(utcTime.AddHours(1).AddHours(1), adjustedTimeHours);
        Assert.AreEqual(utcTime.AddHours(1).AddDays(1), adjustedTimeDays);
    }

    [Test]
    [Category("TimeZoneHelper")]
    public void CheckRepeatScheduleIsStableOverDSTForward()
    {
        var timeZone = TimeZoneHelper.FindTimeZone(CETName);
        var localTime = new DateTimeOffset(2024, 3, 30, 14, 0, 0, timeZone.BaseUtcOffset);
        var localTimeOfDay = localTime.TimeOfDay;

        var utcTime = localTime.ToUniversalTime().DateTime;

        var adjustedTimeSeconds = Timeparser.DSTAwareParseTimeInterval("2s", utcTime, timeZone, false);
        var adjustedTimeHours = Timeparser.DSTAwareParseTimeInterval("1h", utcTime, timeZone, false);
        var adjustedTimeDays = Timeparser.DSTAwareParseTimeInterval("1D", utcTime, timeZone, true);

        // Non-day-based times are not corrected for DST
        Assert.AreEqual(utcTime.AddSeconds(2), adjustedTimeSeconds);
        Assert.AreEqual(utcTime.AddHours(1), adjustedTimeHours);
        // Day-based times are corrected for DST
        Assert.AreEqual(utcTime.AddDays(1).AddHours(-1), adjustedTimeDays);

        // Since we adjust and keep the time of day, the time should be the same
        var adjustedLocalTimeDays = TimeZoneInfo.ConvertTimeFromUtc(adjustedTimeDays, timeZone);
        var adjustedLocalTimeOfDay = adjustedLocalTimeDays.TimeOfDay;
        Assert.AreEqual(localTimeOfDay, adjustedLocalTimeOfDay);

        var origTimeSeconds = timeZone.DSTAwareAddSeconds(adjustedTimeSeconds, -2);
        var origTimeHours = timeZone.DSTAwareAddHours(adjustedTimeHours, -1);
        var origTimeDays = timeZone.DSTAwareAddDays(adjustedTimeDays, -1);
        Assert.AreEqual(utcTime, origTimeSeconds);
        Assert.AreEqual(utcTime, origTimeHours);
        Assert.AreEqual(utcTime, origTimeDays);

        // Since we adjust and keep the time of day, the time should be the same, even when going back
        adjustedLocalTimeDays = TimeZoneInfo.ConvertTimeFromUtc(adjustedTimeDays, timeZone);
        adjustedLocalTimeOfDay = adjustedLocalTimeDays.TimeOfDay;
        Assert.AreEqual(localTimeOfDay, adjustedLocalTimeOfDay);
    }

    [Test]
    [Category("TimeZoneHelper")]
    public void CheckRepeatScheduleIsStableOverDSTBackward()
    {
        var timeZone = TimeZoneHelper.FindTimeZone(CETName);
        var dstOffset = timeZone.GetUtcOffset(new DateTimeOffset(2024, 10, 26, 14, 0, 0, timeZone.BaseUtcOffset));
        var localTime = new DateTimeOffset(2024, 10, 26, 14, 0, 0, dstOffset);
        var crossTime = new DateTimeOffset(2024, 10, 27, 2, 59, 59, dstOffset);
        var localTimeOfDay = localTime.TimeOfDay;

        var utcTime = localTime.ToUniversalTime().DateTime;
        var utcCrossTime = crossTime.ToUniversalTime().DateTime;

        var adjustedTimeSeconds = Timeparser.DSTAwareParseTimeInterval("2s", utcCrossTime, timeZone, false);
        var adjustedTimeHours = Timeparser.DSTAwareParseTimeInterval("1h", utcCrossTime, timeZone, false);
        var adjustedTimeDays = Timeparser.DSTAwareParseTimeInterval("1D", utcTime, timeZone, true);

        // Non-day-based times are not corrected for DST
        Assert.AreEqual(utcCrossTime.AddSeconds(2), adjustedTimeSeconds);
        Assert.AreEqual(utcCrossTime.AddHours(1), adjustedTimeHours);
        // Day-based times are corrected for DST
        Assert.AreEqual(utcTime.AddHours(1).AddDays(1), adjustedTimeDays);

        // Since we adjust and keep the time of day, the time should be the same
        var adjustedLocalTimeDays = TimeZoneInfo.ConvertTimeFromUtc(adjustedTimeDays, timeZone);
        var adjustedLocalTimeOfDay = adjustedLocalTimeDays.TimeOfDay;
        Assert.AreEqual(localTimeOfDay, adjustedLocalTimeOfDay);

        var origTimeDays = timeZone.DSTAwareAddDays(adjustedTimeDays, -1);
        Assert.AreEqual(utcTime, origTimeDays);

        // Since we adjust and keep the time of day, the time should be the same, even when going back
        adjustedLocalTimeDays = TimeZoneInfo.ConvertTimeFromUtc(adjustedTimeDays, timeZone);
        adjustedLocalTimeOfDay = adjustedLocalTimeDays.TimeOfDay;
        Assert.AreEqual(localTimeOfDay, adjustedLocalTimeOfDay);
    }
}
