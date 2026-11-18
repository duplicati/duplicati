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

#nullable enable

using System.Collections.Generic;
using System.Globalization;
using Duplicati.Library.Backend;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The value of the SMB transport option must be matched the same way in every culture.
    /// </summary>
    [TestFixture]
    public class SMBTransportOptionTests
    {
        [TestCase("tr-TR", "NETBIOS")]
        [TestCase("tr-TR", "NetBIOS")]
        [TestCase("tr-TR", "DirectTCP")]
        [TestCase("en-US", "NETBIOS")]
        public void TheTransportIsMatchedInEveryCulture(string cultureName, string transport)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(cultureName, false);
                var options = new Dictionary<string, string?> { ["transport"] = transport };
                Assert.DoesNotThrow(() => new SMBBackend("smb://server/share/folder", options).Dispose());
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }
    }
}
