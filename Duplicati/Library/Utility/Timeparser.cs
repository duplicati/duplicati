#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Utility
{

    /// <summary>
    /// Utility class to parse date/time offset strings like duplicity does:
    /// http://www.nongnu.org/duplicity/duplicity.1.html#sect7
    /// </summary>
    public static class Timeparser
    {
        private static readonly List<string> MONTH_NAMES = new List<string>(new string[] { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" });
    
        public static TimeSpan ParseTimeSpan(string datestring)
        {
            DateTime dt = new DateTime(0);
            return ParseTimeInterval(datestring, dt) - dt;
        }

        public static DateTime ParseTimeInterval(string datestring, DateTime offset)
        {
            return ParseTimeInterval(datestring, offset, false);
        }

        public static DateTime ParseTimeInterval(string datestring, DateTime offset, bool negate)
        {

            int multiplier = negate ? -1 : 1;

            if (string.IsNullOrEmpty(datestring)) 
                return offset;

            if (datestring.Trim().ToLower() == "now")
                return DateTime.Now;

            long l;
            if (long.TryParse(datestring, System.Globalization.NumberStyles.Integer, null, out l))
                return offset.AddSeconds(l * multiplier);
            
            DateTime t;
            if (DateTime.TryParse(datestring, System.Globalization.CultureInfo.CurrentUICulture, System.Globalization.DateTimeStyles.None, out t))
                return t;

            char[] separators = new char[] { 's', 'm', 'h', 'D', 'W', 'M', 'Y' };

            int index = 0;
            int previndex = 0;

            while ((index = datestring.IndexOfAny(separators, previndex)) > 0)
            {
                string partial = datestring.Substring(previndex, index - previndex).Trim();
                int factor;
                if (!int.TryParse(partial, System.Globalization.NumberStyles.Integer, null, out factor))
                    throw new Exception(string.Format(Strings.Timeparser.InvalidIntegerError, partial));

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
                        throw new Exception(string.Format(Strings.Timeparser.InvalidSpecifierError, datestring[index]));
                }
                previndex = index + 1;    
            }

            if (datestring.Substring(previndex).Trim().Length > 0)
                throw new Exception(string.Format(Strings.Timeparser.UnparsedDataFragmentError, datestring.Substring(previndex)));

            return offset;
        }

        public static DateTime ParseDuplicityFileTime(string filetime)
        {
            string[] parts = filetime.Trim().Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);

            int mon = MONTH_NAMES.IndexOf(parts[1].Trim().ToLower());
            if (mon < 0)
                throw new Exception(string.Format(Strings.Timeparser.UnknownMonthError, filetime, parts[1]));

            mon++;

            int day;
            int year;
            if (!int.TryParse(parts[2].Trim(), out day) || day < 0)
                throw new Exception(string.Format(Strings.Timeparser.InvalidDayError, filetime, parts[2]));

            if (!int.TryParse(parts[4].Trim(), out year) || year < 0)
                throw new Exception(string.Format(Strings.Timeparser.InvalidYearError, filetime, parts[4]));

            DateTime t;
            if (!DateTime.TryParse(parts[3].Trim(), null, System.Globalization.DateTimeStyles.NoCurrentDateDefault, out t))
                throw new Exception(string.Format(Strings.Timeparser.InvalidTimeError, filetime, parts[3]));

            return new DateTime(year, mon, day).Add(t.TimeOfDay);

        }
    }
}
