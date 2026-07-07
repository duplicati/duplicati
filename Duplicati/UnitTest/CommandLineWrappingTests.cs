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

using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class CommandLineWrappingTests
    {
        [Test]
        [Category("Utility")]
        public static void WrapCommandLineElementEscapesWindowsPercentWhenEnvironmentExpansionIsDisabled()
        {
            Assert.AreEqual("\"abc%%TEMP%%def\"", Utility.WrapCommandLineElement("abc%TEMP%def", false, true));
            Assert.AreEqual("\"100%%\"", Utility.WrapCommandLineElement("100%", false, true));
        }

        [Test]
        [Category("Utility")]
        public static void WrapCommandLineElementKeepsWindowsPercentWhenEnvironmentExpansionIsEnabled()
        {
            Assert.AreEqual("\"abc%TEMP%def\"", Utility.WrapCommandLineElement("abc%TEMP%def", true, true));
        }

        [Test]
        [Category("Utility")]
        public static void WrapCommandLineElementKeepsExistingWindowsQuotingBehavior()
        {
            Assert.AreEqual("\"has\"\"quote\"", Utility.WrapCommandLineElement("has\"quote", false, true));
            Assert.AreEqual("\"C:\\Temp\\folder\\\\\"", Utility.WrapCommandLineElement("C:\\Temp\\folder\\", false, true));
        }

        [Test]
        [Category("Utility")]
        public static void WrapCommandLineElementRespectsUnixEnvironmentExpansionFlag()
        {
            Assert.AreEqual("\"$HOME/file\"", Utility.WrapCommandLineElement("$HOME/file", true, false));
            Assert.AreEqual("\"\\$HOME/file\"", Utility.WrapCommandLineElement("$HOME/file", false, false));
        }
    }
}
