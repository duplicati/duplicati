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

using Duplicati.Library.Encryption;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class AESStringEncryptionTests : BasicSetupHelper
    {
        [Test]
        [Category("AESStringEncryption")]
        public static void EncryptDecryptAndCompare()
        {
            string data = "Sample Data to Encrypt";
            string passphrase = "Secret key used to encrypt";

            string encrypted = AESStringEncryption.EncryptToHex(passphrase, data);

            Assert.IsNotNull(encrypted);
            Assert.IsNotEmpty(encrypted);

            string decrypted = AESStringEncryption.DecryptFromHex(passphrase, encrypted);

            Assert.IsNotNull(decrypted);
            Assert.IsNotEmpty(decrypted);
            Assert.AreEqual(decrypted, data);

        }

    }
}
