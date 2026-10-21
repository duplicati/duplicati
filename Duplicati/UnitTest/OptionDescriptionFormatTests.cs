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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Duplicati.Library.Main;
using NUnit.Framework;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The --vss-provider-id description contains a GUID in braces. With the
    /// invariant culture (which the commandline help forces) the description goes
    /// through string.Format, and the bare braces made every lookup of the options
    /// throw a FormatException on Windows. Other cultures do not format a string
    /// without arguments, so doubling the braces would show them doubled there.
    /// </summary>
    public class OptionDescriptionFormatTests
    {
        [Test]
        [Category("Options")]
        [TestCase("")]
        [TestCase("en-US")]
        public void VssProviderIdDescriptionShowsTheGuidInBraces(string culture)
        {
            if (!OperatingSystem.IsWindows())
                Assert.Ignore("The VSS options are only offered on Windows");

            var previousCulture = CultureInfo.CurrentCulture;
            var previousUICulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);

                var option = new Options(new Dictionary<string, string?>()).SupportedCommands.Single(x => x.Name == "vss-provider-id");

                StringAssert.Contains(" {b5946137-7b9f-4925-af80-51abd60b20d5} ", option.LongDescription);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUICulture;
            }
        }
    }
}
