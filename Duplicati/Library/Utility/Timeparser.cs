// Copyright (C) 2024, The Duplicati Team
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
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Utility
{

    /// <summary>
    /// Utility class to parse date/time offset strings like duplicity does:
    /// http://duplicity.nongnu.org/vers8/duplicity.1.html#sect8
    /// </summary>
    public static class Timeparser
    {
        public static TimeSpan ParseTimeSpan(string datestring)
        {
            DateTime dt = new DateTime(0, DateTimeKind.Local);
            return ParseTimeInterval(datestring, dt) - dt;
        }

        public static DateTime ParseTimeInterval(string datestring, DateTime offset)
        {
            return ParseTimeInterval(datestring, offset, false);
        }

        public static DateTime ParseTimeInterval(string datestring, DateTime offset, bool negate)
        {
            if (offset.Kind == DateTimeKind.Unspecified)
                offset = new DateTime(offset.Ticks, DateTimeKind.Local);

            int multiplier = negate ? -1 : 1;

            if (string.IsNullOrEmpty(datestring)) 
                return offset;

            if (String.Equals(datestring.Trim(), "now", StringComparison.OrdinalIgnoreCase))
                return DateTime.Now;

            long l;
            if (long.TryParse(datestring, System.Globalization.NumberStyles.Integer, null, out l))
                return offset.AddSeconds(l * multiplier);
            
            DateTime t;
            if (DateTime.TryParse(datestring, System.Globalization.CultureInfo.CurrentUICulture, System.Globalization.DateTimeStyles.AssumeLocal, out t))
                return t;
            
            if (Library.Utility.Utility.TryDeserializeDateTime(datestring, out t))
                return t;

            char[] separators = new char[] { 's', 'm', 'h', 'D', 'W', 'M', 'Y' };

            int index = 0;
            int previndex = 0;

            while ((index = datestring.IndexOfAny(separators, previndex)) > 0)
            {
                string partial = datestring.Substring(previndex, index - previndex).Trim();
                int factor;
                if (!int.TryParse(partial, System.Globalization.NumberStyles.Integer, null, out factor))
                    throw new Exception(Strings.Timeparser.InvalidIntegerError(partial));

                factor *= multiplier;

                switch (datestring[index])
                {
                    case 's':
                        offset = offset.AddSeconds(factor);
                        break;
                    case 'm':
                        offset = offset.AddMinutes(factor);
                        break;
                    case 'h':
                        offset = offset.AddHours(factor);
                        break;
                    case 'D':
                        offset = offset.AddDays(factor);
                        break;
                    case 'W':
                        offset = offset.AddDays(factor * 7);
                        break;
                    case 'M':
                        offset = offset.AddMonths(factor);
                        break;
                    case 'Y':
                        offset = offset.AddYears(factor);
                        break;
                    default:
                        throw new Exception(Strings.Timeparser.InvalidSpecifierError(datestring[index]));
                }
                previndex = index + 1;    
            }

            if (datestring.Substring(previndex).Trim().Length > 0)
                throw new Exception(Strings.Timeparser.UnparsedDataFragmentError(datestring.Substring(previndex)));

            return offset;
        }
    }
}
