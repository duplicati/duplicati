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
    /// Common interface for a filter
    /// </summary>
    public interface IFilter
    {
        /// <summary>
        /// Gets a value indicating whether this <see cref="Duplicati.Library.Utility.IFilter"/> is empty.
        /// </summary>
        /// <value><c>true</c> if empty; otherwise, <c>false</c>.</value>
        bool Empty { get; }
        /// <summary>
        /// Performs a test to see if the entry matches the filter
        /// </summary>
        /// <param name="entry">The entry to match</param>
        /// <param name="result">The match result</param>
        /// <param name="match">The filter that matched</param>
        bool Matches(string entry, out bool result, out IFilter match);

        /// <summary>
        /// Returns a MD5 hash string representing the filter
        /// </summary>
        /// <returns></returns>
        string GetFilterHash();
    }
}

