using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Core
{
    public static class Sizeparser
    {
        public static long ParseSize(string size, string defaultSuffix)
        {
            if (size.EndsWith("gb") || size.EndsWith("mb") || size.EndsWith("kb") || size.EndsWith("b"))
                return ParseSize(size);
            else
                return ParseSize(size + " " + defaultSuffix);
        }

        public static long ParseSize(string size)
        {
            if (size == null)
                return 0;
            string origsize = size;

            size = size.Trim().ToLower();
            
            long factor = 1;

            if (size.EndsWith("gb"))
            {
                factor = 1024 * 1024 * 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("mb"))
            {
                factor = 1024 * 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("kb"))
            {
                factor = 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("b"))
                size = size.Substring(0, size.Length - 1).Trim();

            long r;
            if (!long.TryParse(size, out r))
                throw new Exception("Invalid size value: " + origsize);
            else
                return factor * r;
        }
    }
}
