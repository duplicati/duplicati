#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
    public static class Sizeparser
    {
        public static long ParseSize(string size, string defaultSuffix)
        {
            if (string.IsNullOrEmpty(size))
                return 0;

            size = size.Trim();

            if (size.EndsWith("tb", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("gb", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("mb", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("kb", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("b", StringComparison.OrdinalIgnoreCase))
                return ParseSize(size);
            else
                return ParseSize(size + " " + defaultSuffix);
        }

        public static long ParseSize(string size)
        {
            if (string.IsNullOrEmpty(size))
                return 0;

            string origsize = size;

            size = size.Trim();
            
            long factor = 1;

            if (size.EndsWith("tb", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024L * 1024 * 1024 * 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("gb", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024 * 1024 * 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("mb", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024 * 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("kb", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("b", StringComparison.OrdinalIgnoreCase))
                size = size.Substring(0, size.Length - 1).Trim();

            long r;
            if (!long.TryParse(size, out r))
                throw new Exception(Strings.Sizeparser.InvalidSizeValueError(origsize));
            else
                return factor * r;
        }
    }
}
