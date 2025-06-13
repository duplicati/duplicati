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
}
