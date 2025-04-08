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
using System.Text.RegularExpressions;

namespace Duplicati.Library.Utility
{

    /// <summary>
    /// Utility class to parse relative date/time offset strings
    /// </summary>
    public static class Timeparser
    {
        /// <summary>
        /// Parses a time interval string and returns a TimeSpan object
        /// </summary>
        /// <param name="datestring">The time interval string</param>
        /// <returns>The calculated TimeSpan</returns>
        public static TimeSpan ParseTimeSpan(string datestring)
            => ParseTimeSpan(datestring, false);

        /// <summary>
        /// Parses a time interval string and returns a TimeSpan object
        /// </summary>
        /// <param name="datestring">The time interval string</param>
        /// <param name="negate">True if the interval should be negated from the input</param>
        /// <returns>The calculated TimeSpan</returns>
        public static TimeSpan ParseTimeSpan(string datestring, bool negate)
        {
            var multiplier = negate ? -1 : 1;
            var offset = TimeSpan.Zero;

            if (string.IsNullOrEmpty(datestring))
                return offset;

            if (long.TryParse(datestring, System.Globalization.NumberStyles.Integer, null, out var l))
                return offset.Add(TimeSpan.FromSeconds(l * multiplier));

            var separators = new char[] { 's', 'm', 'h', 'D', 'W', 'M', 'Y' };

            int index;
            var previndex = 0;

            while ((index = datestring.IndexOfAny(separators, previndex)) > 0)
            {
                var partial = datestring.Substring(previndex, index - previndex).Trim();
                if (!int.TryParse(partial, System.Globalization.NumberStyles.Integer, null, out var factor))
                    throw new Exception(Strings.Timeparser.InvalidIntegerError(partial));

                factor *= multiplier;

                offset += datestring[index] switch
                {
                    's' => TimeSpan.FromSeconds(factor),
                    'm' => TimeSpan.FromMinutes(factor),
                    'h' => TimeSpan.FromHours(factor),
                    'D' => TimeSpan.FromDays(factor),
                    'W' => TimeSpan.FromDays(factor * 7),
                    'M' => TimeSpan.FromDays(factor * 30),
                    'Y' => TimeSpan.FromDays(factor * 365),
                    _ => throw new Exception(Strings.Timeparser.InvalidSpecifierError(datestring[index])),
                };
                previndex = index + 1;
            }

            if (datestring.Substring(previndex).Trim().Length > 0)
                throw new Exception(Strings.Timeparser.UnparsedDataFragmentError(datestring.Substring(previndex)));

            return offset;
        }

        /// <summary>
        /// Matches a time interval string
        /// </summary>
        private static readonly Regex _timeIntervalRegex = new Regex(@"^(\d+[smhDWMY])+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Parses a time interval string and returns a DateTime object
        /// </summary>
        /// <param name="datestring">The time interval string</param>
        /// <param name="offset">>The base time to add the interval to</param>
        /// <returns>The calculated DateTime</returns>
        /// <remarks>Note: The offset is assumed to be in local time if unspecified</remarks>
        public static DateTime ParseTimeInterval(string datestring, DateTime offset)
            => ParseTimeInterval(datestring, offset, false);

        /// <summary>
        /// Parses a time interval string and returns a DateTime object
        /// </summary>
        /// <param name="datestring">>The time interval string</param>
        /// <param name="offset">>The base time to add the interval to</param>
        /// <param name="negate">True if the interval should be subtracted</param>
        /// <returns>>The calculated DateTime</returns>
        /// <remarks>Note: The offset is assumed to be in local time if unspecified</remarks>
        public static DateTime ParseTimeInterval(string datestring, DateTime offset, bool negate)
        {
            if (string.IsNullOrEmpty(datestring))
                return offset;

            if (offset.Kind == DateTimeKind.Unspecified)
                offset = new DateTime(offset.Ticks, DateTimeKind.Local);

            var multiplier = negate ? -1 : 1;

            if (_timeIntervalRegex.IsMatch(datestring))
            {
                var separators = new char[] { 's', 'm', 'h', 'D', 'W', 'M', 'Y' };

                int index;
                var previndex = 0;

                while ((index = datestring.IndexOfAny(separators, previndex)) > 0)
                {
                    var partial = datestring.Substring(previndex, index - previndex).Trim();
                    if (!int.TryParse(partial, System.Globalization.NumberStyles.Integer, null, out var factor))
                        throw new Exception(Strings.Timeparser.InvalidIntegerError(partial));

                    factor *= multiplier;

                    offset = datestring[index] switch
                    {
                        's' => offset.AddSeconds(factor),
                        'm' => offset.AddMinutes(factor),
                        'h' => offset.AddHours(factor),
                        'D' => offset.AddDays(factor),
                        'W' => offset.AddDays(factor * 7),
                        'M' => offset.AddMonths(factor),
                        'Y' => offset.AddYears(factor),
                        _ => throw new Exception(Strings.Timeparser.InvalidSpecifierError(datestring[index])),
                    };
                    previndex = index + 1;
                }

                if (datestring.Substring(previndex).Trim().Length > 0)
                    throw new Exception(Strings.Timeparser.UnparsedDataFragmentError(datestring.Substring(previndex)));

                return offset;
            }

            if (string.Equals(datestring.Trim(), "now", StringComparison.OrdinalIgnoreCase))
                return DateTime.Now;

            if (long.TryParse(datestring, System.Globalization.NumberStyles.Integer, null, out var l))
                return offset.AddSeconds(l * multiplier);

            if (DateTime.TryParse(datestring, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var t))
                return t;

            if (Utility.TryDeserializeDateTime(datestring, out t))
                return t;

            throw new Exception(Strings.Timeparser.InvalidDateTimeError(datestring));
        }

        /// <summary>
        /// Parses a time interval string with a timezone offset, retaining the local time
        /// </summary>
        /// <param name="datestring">The repeating interval string</param>
        /// <param name="offset">The base time to add the interval to</param>
        /// <param name="timeZoneInfo">The timezone to use for the calculation</param>
        /// <param name="negate">True if the interval should be subtracted</param>
        /// <returns>The calculated time</returns>
        public static DateTime DSTAwareParseTimeInterval(string datestring, DateTime rawoffset, TimeZoneInfo timeZoneInfo, bool negate = false)
        {
            if (string.IsNullOrEmpty(datestring))
                return rawoffset;

            var result = new DateTimeOffset(rawoffset.Kind == DateTimeKind.Local ? rawoffset.ToUniversalTime() : rawoffset, TimeSpan.Zero);

            int multiplier = negate ? -1 : 1;

            var before = result;
            if (long.TryParse(datestring, System.Globalization.NumberStyles.Integer, null, out var l))
                datestring = $"{l}s";

            if (_timeIntervalRegex.IsMatch(datestring))
            {
                var separators = new char[] { 's', 'm', 'h', 'D', 'W', 'M', 'Y' };

                int index;
                var previndex = 0;
                var keepTimeOfDay = true;

                while ((index = datestring.IndexOfAny(separators, previndex)) > 0)
                {
                    var partial = datestring.Substring(previndex, index - previndex).Trim();
                    if (!int.TryParse(partial, System.Globalization.NumberStyles.Integer, null, out var factor))
                        throw new Exception(Strings.Timeparser.InvalidIntegerError(partial));

                    if (datestring[index] == 's' || datestring[index] == 'm' || datestring[index] == 'h')
                        keepTimeOfDay = false;

                    factor *= multiplier;
                    result = datestring[index] switch
                    {
                        's' => result.AddSeconds(factor),
                        'm' => result.AddMinutes(factor),
                        'h' => result.AddHours(factor),
                        'D' => result.AddDays(factor),
                        'W' => result.AddDays(factor * 7),
                        'M' => result.AddMonths(factor),
                        'Y' => result.AddYears(factor),
                        _ => throw new Exception(Strings.Timeparser.InvalidSpecifierError(datestring[index])),
                    };
                    previndex = index + 1;
                }

                if (datestring.Substring(previndex).Trim().Length > 0)
                    throw new Exception(Strings.Timeparser.UnparsedDataFragmentError(datestring.Substring(previndex)));

                var beforeLocal = TimeZoneInfo.ConvertTime(before, timeZoneInfo);
                var resultLocal = TimeZoneInfo.ConvertTime(result, timeZoneInfo);
                if (keepTimeOfDay && beforeLocal.Offset != resultLocal.Offset)
                {
                    var diff = beforeLocal.Offset - resultLocal.Offset;
                    result = result.Add(diff);
                }

                return result.ToUniversalTime().UtcDateTime;
            }

            if (string.Equals(datestring.Trim(), "now", StringComparison.OrdinalIgnoreCase))
                return DateTime.UtcNow;

            if (DateTime.TryParse(datestring, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var t))
                return t;

            if (Utility.TryDeserializeDateTime(datestring, out t))
                return t;

            throw new Exception(Strings.Timeparser.InvalidDateTimeError(datestring));
        }
    }
}
