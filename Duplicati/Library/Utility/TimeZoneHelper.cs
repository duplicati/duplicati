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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.Library.Utility;

/// <summary>
/// Adds the specified number of minutes to the given DateTime, taking into account the time zone's daylight saving time rules.
/// A helper class to get the time zone information
/// </summary>
public static class TimeZoneHelper
{
    /// <summary>
    /// A record to display the time zone
    /// </summary>
    /// <param name="Id">The time zone id</param>
    /// <param name="DisplayName">The display name of the time zone</param>
    /// <param name="CurrentUtcOffset">The base UTC offset of the time zone</param>
    public record TimeZoneDisplay(string Id, string DisplayName, TimeSpan CurrentUtcOffset);

    /// <summary>
    /// Get the local (system) time zone
    /// </summary>
    /// <returns>The local time zone</returns>
    public static string GetLocalTimeZone()
        => TimeZoneInfo.Local.Id;

    /// <summary>
    /// Get the time zones on this system
    /// </summary>
    /// <returns>The time zones</returns>
    public static IEnumerable<TimeZoneDisplay> GetTimeZones()
    {
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
            yield return new TimeZoneDisplay(tz.Id, tz.DisplayName, tz.GetUtcOffset(DateTime.Now));
    }

    /// <summary>
    /// Get the time zone by id
    /// </summary>
    /// <param name="id">The time zone id</param>
    /// <returns>The time zone</returns>
    public static TimeZoneInfo? GetTimeZoneById(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
        }
        return null;
    }

    /// <summary>
    /// Search for a time zone
    /// </summary>
    /// <param name="search">The search string</param>
    /// <returns>The time zone</returns>
    public static TimeZoneInfo? FindTimeZone(string search)
    {
        var tzi = GetTimeZoneById(search);
        if (tzi != null)
            return tzi;

        return TimeZoneInfo.GetSystemTimeZones()
                .FirstOrDefault(tz => tz.Id.Equals(search, StringComparison.OrdinalIgnoreCase) || tz.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Corrects the time for daylight saving time, by checking if the offset has changed
    /// </summary>
    /// <param name="timeZoneInfo">The timezone to use for the correction</param>
    /// <param name="before">The time before in UTC</param>
    /// <param name="after">The time after in UTC</param>
    /// <returns>The corrected time in UTC</returns>
    public static DateTime DSTAwareTimeAdjust(this TimeZoneInfo timeZoneInfo, DateTime before, DateTime after)
    {
        var beforeLocal = TimeZoneInfo.ConvertTime(new DateTimeOffset(before, TimeSpan.Zero), timeZoneInfo);
        var afterLocal = TimeZoneInfo.ConvertTime(new DateTimeOffset(after, TimeSpan.Zero), timeZoneInfo);

        if (beforeLocal.Offset == afterLocal.Offset)
            return after;

        var diff = beforeLocal.Offset - afterLocal.Offset;
        return after.Add(diff);
    }

    /// <summary>
    /// Adds seconds to a time in a specific time zone
    /// </summary>
    /// <param name="timeZoneInfo">The time zone to use for the calculation</param>
    /// <param name="dt">The time to add seconds to</param>
    /// <param name="seconds">The number of seconds to add</param>
    /// <returns>The new time</returns>
    public static DateTime DSTAwareAddSeconds(this TimeZoneInfo timeZoneInfo, DateTime dt, long seconds)
        => DSTAwareTimeAdjust(timeZoneInfo, dt, dt.AddSeconds(seconds));

    /// <summary>
    /// Adds the specified number of minutes to the given DateTime, taking into account the time zone's daylight saving time rules.
    /// </summary>
    /// <param name="timeZoneInfo">The time zone information.</param>
    /// <param name="dt">The DateTime to which minutes will be added.</param>
    /// <param name="minutes">The number of minutes to add.</param>
    /// <returns>A DateTime that is the result of adding the specified number of minutes to the given DateTime, adjusted for daylight saving time.</returns>
    public static DateTime DSTAwareAddMinutes(this TimeZoneInfo timeZoneInfo, DateTime dt, int minutes)
        => DSTAwareTimeAdjust(timeZoneInfo, dt, dt.AddMinutes(minutes));

    /// <summary>
    /// Adds the specified number of hours to the given DateTime, taking into account the time zone's daylight saving time rules.
    /// </summary>
    /// <param name="timeZoneInfo">The time zone information.</param>
    /// <param name="dt">The DateTime to which hours will be added.</param>
    /// <param name="hours">The number of hours to add.</param>
    /// <returns>A DateTime that is the result of adding the specified number of hours to the given DateTime, adjusted for daylight saving time.</returns>
    public static DateTime DSTAwareAddHours(this TimeZoneInfo timeZoneInfo, DateTime dt, int hours)
        => DSTAwareTimeAdjust(timeZoneInfo, dt, dt.AddHours(hours));

    /// <summary>
    /// Adds the specified number of days to the given DateTime, taking into account the time zone's daylight saving time rules.
    /// </summary>
    /// <param name="timeZoneInfo">The time zone information.</param>
    /// <param name="dt">The DateTime to which days will be added.</param>
    /// <param name="days">The number of days to add.</param>
    /// <returns>A DateTime that is the result of adding the specified number of days to the given DateTime, adjusted for daylight saving time.</returns>
    public static DateTime DSTAwareAddDays(this TimeZoneInfo timeZoneInfo, DateTime dt, int days)
        => DSTAwareTimeAdjust(timeZoneInfo, dt, dt.AddDays(days));

    /// <summary>
    /// Adds the specified number of months to the given DateTime, taking into account the time zone's daylight saving time rules.
    /// </summary>
    /// <param name="timeZoneInfo">The time zone information.</param>
    /// <param name="dt">The DateTime to which months will be added.</param>
    /// <param name="months">The number of months to add.</param>
    /// <returns>A DateTime that is the result of adding the specified number of months to the given DateTime, adjusted for daylight saving time.</returns>
    public static DateTime DSTAwareAddMonths(this TimeZoneInfo timeZoneInfo, DateTime dt, int months)
        => DSTAwareTimeAdjust(timeZoneInfo, dt, dt.AddMonths(months));

    /// <summary>
    /// Adds the specified number of years to the given DateTime, taking into account the time zone's daylight saving time rules.
    /// </summary>
    /// <param name="timeZoneInfo">The time zone information.</param>
    /// <param name="dt">The DateTime to which years will be added.</param>
    /// <param name="years">The number of years to add.</param>
    /// <returns>A DateTime that is the result of adding the specified number of years to the given DateTime, adjusted for daylight saving time.</returns>
    public static DateTime DSTAwareAddYears(this TimeZoneInfo timeZoneInfo, DateTime dt, int years)
        => DSTAwareTimeAdjust(timeZoneInfo, dt, dt.AddYears(years));
}
