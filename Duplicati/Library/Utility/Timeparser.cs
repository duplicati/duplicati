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

namespace Duplicati.Library.Utility
{

    /// <summary>
    /// Utility class to parse date/time offset strings like duplicity does:
    /// http://duplicity.nongnu.org/vers8/duplicity.1.html#sect8
    /// </summary>
    public static class Timeparser
    {
        public static TimeSpan ParseTimeSpan(string datestring)
            => ParseTimeSpan(datestring, false);

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

        public static DateTime ParseTimeInterval(string datestring, DateTime offset)
        {
            return ParseTimeInterval(datestring, offset, false);
        }

        public static DateTime ParseTimeInterval(string datestring, DateTime offset, bool negate)
        {
            if (offset.Kind == DateTimeKind.Unspecified)
                offset = new DateTime(offset.Ticks, DateTimeKind.Local);

            var multiplier = negate ? -1 : 1;

            if (string.IsNullOrEmpty(datestring))
                return offset;

            if (string.Equals(datestring.Trim(), "now", StringComparison.OrdinalIgnoreCase))
                return DateTime.Now;

            if (long.TryParse(datestring, System.Globalization.NumberStyles.Integer, null, out var l))
                return offset.AddSeconds(l * multiplier);

            if (DateTime.TryParse(datestring, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var t))
                return t;

            if (Utility.TryDeserializeDateTime(datestring, out t))
                return t;

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

        /// <summary>
        /// Parses a time interval string with a timezone offset, retaining the local time
        /// </summary>
        /// <param name="datestring">The repeating interval string</param>
        /// <param name="offset">The base time to add the interval to</param>
        /// <param name="timeZoneInfo">The timezone to use for the calculation</param>
        /// <param name="keepTimeOfDay">True if the time of day should be kept across DST changes</param>
        /// <param name="negate">True if the interval should be subtracted</param>
        /// <returns>The calculated time</returns>
        public static DateTime DSTAwareParseTimeInterval(string datestring, DateTime offset, TimeZoneInfo timeZoneInfo, bool keepTimeOfDay, bool negate = false)
        {
            // Use non-DST-aware version if we are not fixing this to the current time-of-day
            if (!keepTimeOfDay)
                return ParseTimeInterval(datestring, offset, negate);

            if (offset.Kind == DateTimeKind.Unspecified)
                offset = new DateTime(offset.Ticks, DateTimeKind.Utc);

            int multiplier = negate ? -1 : 1;

            if (string.IsNullOrEmpty(datestring))
                return offset;

            if (string.Equals(datestring.Trim(), "now", StringComparison.OrdinalIgnoreCase))
                return DateTime.UtcNow;

            if (long.TryParse(datestring, System.Globalization.NumberStyles.Integer, null, out var l))
                return timeZoneInfo.DSTAwareAddSeconds(offset, l * multiplier);

            if (DateTime.TryParse(datestring, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var t))
                return t;

            if (Utility.TryDeserializeDateTime(datestring, out t))
                return t;

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
                    's' => timeZoneInfo.DSTAwareAddSeconds(offset, factor),
                    'm' => timeZoneInfo.DSTAwareAddMinutes(offset, factor),
                    'h' => timeZoneInfo.DSTAwareAddHours(offset, factor),
                    'D' => timeZoneInfo.DSTAwareAddDays(offset, factor),
                    'W' => timeZoneInfo.DSTAwareAddDays(offset, factor * 7),
                    'M' => timeZoneInfo.DSTAwareAddMonths(offset, factor),
                    'Y' => timeZoneInfo.DSTAwareAddYears(offset, factor),
                    _ => throw new Exception(Strings.Timeparser.InvalidSpecifierError(datestring[index])),
                };
                previndex = index + 1;
            }

            if (datestring.Substring(previndex).Trim().Length > 0)
                throw new Exception(Strings.Timeparser.UnparsedDataFragmentError(datestring.Substring(previndex)));

            return offset;
        }
    }
}
