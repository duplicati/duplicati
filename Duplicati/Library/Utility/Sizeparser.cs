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
    public static class Sizeparser
    {
        public static long ParseSize(string size, string defaultSuffix)
        {
            if (string.IsNullOrEmpty(size))
                return 0;

            size = size.ToLower().Trim();

            if (size.EndsWith("gb") || size.EndsWith("mb") || size.EndsWith("kb") || size.EndsWith("b"))
                return ParseSize(size);
            else
                return ParseSize(size + " " + defaultSuffix);
        }

        public static long ParseSize(string size)
        {
            if (string.IsNullOrEmpty(size))
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
                throw new Exception(string.Format(Strings.Sizeparser.InvalidSizeValueError, origsize));
            else
                return factor * r;
        }
    }
}
