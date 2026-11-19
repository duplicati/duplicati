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


using System.Globalization;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A filter that ignores case tells "I" and "i" apart under Turkish culture
    /// if its regular expression is not culture invariant
    /// </summary>
    [NonParallelizable]
    public class FilterCultureTests
    {
        [TestCase(@"[.*\.INI]", "boot.ini")]
        [TestCase(@"[.*\.ini]", "BOOT.INI")]
        [Category("Filter")]
        public void CaseInsensitiveFilterMatchesUnderTurkishCulture(string filter, string path)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                var expression = new FilterExpression(filter, true, false);
                Assert.IsTrue(expression.Matches(path, out var result, out _), filter);
                Assert.IsTrue(result, filter);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
