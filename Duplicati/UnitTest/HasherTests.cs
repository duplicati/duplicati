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

using NUnit.Framework;
using System.Security.Cryptography;

namespace Duplicati.UnitTest
{
    public class HashExtentions : BasicSetupHelper
    {
        [Test]
        [Category("HashExtentions")]
        public static void ComputeHashes()
        {
            string data = "FIXED DATA WITH KNOWN SHA256 HASH";
            string expectedHash = "A4B902FA51819A29890225999C542D8BD6B5499E96B6B581E43B04FE46C39B12";

            string hashed = data.ComputeHashToHex(SHA256.Create());

            Assert.IsNotNull(hashed);
            Assert.IsNotEmpty(hashed);
            Assert.AreEqual(expectedHash, hashed);

        }

    }
}
