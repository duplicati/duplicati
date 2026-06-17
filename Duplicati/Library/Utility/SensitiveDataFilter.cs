// Copyright (C) 2026, The Duplicati Team
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
    /// Helper class to filter sensitive data from strings
    /// </summary>
    public static class SensitiveDataFilter
    {
        /// <summary>
        /// Regex that detects file system paths in text
        /// </summary>
        private static readonly Regex PathDetectionRegex = new Regex(
            @"(?<![\w/\\])" +
            @"(?:" +
                @"(?:[a-zA-Z]:\\(?:[^\\\s\n\r]*\\?)*)" +       // Windows drive paths
                @"|(?:\\\\[^\\\s\n\r]+(?:\\[^\\\s\n\r]*)*)" +     // UNC paths
                @"|(?:/[a-zA-Z0-9_.][^/\s\n\r]*(?:/[a-zA-Z0-9_.][^/\s\n\r]*)*)" + // Unix absolute paths
                @"|(?:file:///[^\s\n\r]*)" +                       // File URIs
            @")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        /// <summary>
        /// Characters that are commonly trailing delimiters and should not be part of a path
        /// </summary>
        private static readonly char[] PathTrimChars = new[] { '"', '\'', '(', ')', '[', ']', '{', '}', '<', '>', ',', ';', ':' };

        /// <summary>
        /// Replaces detected file system paths with "-redacted-"
        /// </summary>
        /// <param name="input">The input string to filter</param>
        /// <returns>The filtered string with paths redacted</returns>
        public static string RedactPaths(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return PathDetectionRegex.Replace(input, match =>
            {
                var value = match.Value;

                // Trim trailing punctuation that is likely not part of the path
                int end = value.Length;
                while (end > 0 && Array.IndexOf(PathTrimChars, value[end - 1]) >= 0)
                    end--;

                if (end < value.Length)
                    return "-redacted-" + value.Substring(end);

                return "-redacted-";
            });
        }
    }
}
