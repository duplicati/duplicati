using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility
{
    public static class DateTimeInvariant
    {
        private static string _Format = "yyyyMMdd'T'HHmmssK";
        public static DateTime ParseExact(string s, DateTimeStyles style)
        {
            try
            {
                return DateTime.ParseExact(s, _Format, DateTimeFormatInfo.InvariantInfo, style);
            }
            catch (Exception) //For compatibility with previous backed-up data using system specific culture (other than Gregorian calendar)
            {
                return DateTime.ParseExact(s, _Format, null, style);
            }
        }

        public static bool TryParseExact(string s, DateTimeStyles style, out DateTime result)
        {
            bool r = DateTime.TryParseExact(s, _Format, DateTimeFormatInfo.InvariantInfo, style, out result);
            if (!r) //For compatibility with previous backed-up data using system specific culture
                r = DateTime.TryParseExact(s, _Format, null, style, out result);
            return r;
        }

        public static string ToStringInvariant(this DateTime date)
        {
            return date.ToString(_Format, DateTimeFormatInfo.InvariantInfo);
        }
    }
}
