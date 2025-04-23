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
                size.EndsWith("tib", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("gb", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("gib", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("mb", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("mib", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("kb", StringComparison.OrdinalIgnoreCase) ||
                size.EndsWith("kib", StringComparison.OrdinalIgnoreCase) ||
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
            if (size.EndsWith("tib", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024L * 1024 * 1024 * 1024;
                size = size.Substring(0, size.Length - 3).Trim();
            }
            else if (size.EndsWith("gb", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024 * 1024 * 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("gib", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024 * 1024 * 1024;
                size = size.Substring(0, size.Length - 3).Trim();
            }
            else if (size.EndsWith("mb", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024 * 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("mib", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024 * 1024;
                size = size.Substring(0, size.Length - 3).Trim();
            }
            else if (size.EndsWith("kb", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024;
                size = size.Substring(0, size.Length - 2).Trim();
            }
            else if (size.EndsWith("kib", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1024;
                size = size.Substring(0, size.Length - 3).Trim();
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
