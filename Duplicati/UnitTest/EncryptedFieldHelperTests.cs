// Copyright (C) 2024, The Duplicati Team
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
using System.CodeDom;
using System.Linq;
using Duplicati.Library.Encryption;
using Duplicati.Library.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class EncryptedFieldHelperTests : BasicSetupHelper
    {

        [Test]
        [Category("FieldEncryption")]
        public static void TestWithBothEncryptedAndNonEncrypted()
        {

            // This password was used to compute the encrypted value, so it should not be changed.
            string encryptionKeyForTest = "long and good password";
            string sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";
            string sampleEncryptedTargerURL = "5594912DBD85E5105168FDB0BFFE0AACB755BA3AF9E7A9AAA43F7C0804E0F6FE138E362F5B564379CDE40F73BC96D4EAB0B949CC82A592D0194040FA08DD49B241455302000021435245415445445F425900536861727041455343727970742076322E302E322E300017435245415445445F4441544500323032342D30382D31340015435245415445445F54494D450030322D31362D3136008000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000DDB640CCD2B7D29CEA9D7143C5C47830608D86616863E1200545D12EAD5A6CE11ED2E7677549AEF5B5152DBBE196E5CF09E13E751F2979A02F45827C58AF7979EC9618603D4A163FE46FA1BF326826A0EC021C63EA2789FC540B52F44F1054FE53ED8CB131A7B237D4E591EA5580FC0BD2BC0F1EB3071A7BD25A357193826C0BCC75A6883E643B67169D52CD5D500F32A0B565B07035F7C07637186A921E49869BFD8F49EE3296F7DEFC654F31AD6DA93BF3254303D5F3C545E29023F6DA9EF85176F4642CC5B09582E79671516A1EFE8FA12F6809C683CBF49A2DE1C90D6B1A462BF1F3B32A9E6C120CB8C64865471709659C8B06204F676EDAD665E639482BAB9C2ABD3B52E58B8A421724149A262D2D";

            Environment.SetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY", encryptionKeyForTest);

            // Sample URL is not encrypted, so it should not suffer transformation and be returned as is
            Assert.AreEqual(EncryptedFieldHelper.Decrypt(sampleTargerURL), sampleTargerURL);

            // SampleEncrypted URL is encrypted, so it should be decrypted and returned matching the unencrypted version
            Assert.AreEqual(EncryptedFieldHelper.Decrypt(sampleEncryptedTargerURL), sampleTargerURL);

        }

        [Test]
        [Category("FieldEncryption")]
        public static void TestTamperingFirstHash()
        {

            string encryptionKeyForTest = "long and good password";
            string sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";
      
            Environment.SetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY", encryptionKeyForTest);

            var sampleEncryptedTargerURL = EncryptedFieldHelper.Encrypt(sampleTargerURL);
            // Tampering now with the first bytes of encrypted string, which is the content hash,
        
            var tamperingTest1 = $"{sampleEncryptedTargerURL.Substring(0, 64).Reverse()}{sampleEncryptedTargerURL.Substring(64)}";

            var tamperedDecryption = EncryptedFieldHelper.Decrypt(tamperingTest1);

            // Because the hash is tampered, the EncryptedFieldHelper will not perceive the record as a valid encrypted field, and will return
            // as is, so the returned value should be equal to the tampered value

            Assert.AreEqual(tamperedDecryption, tamperingTest1);

        }

        [Test]
        [Category("FieldEncryption")]
        public static void TestTamperingKeyHash()
        {
            string encryptionKeyForTest = "long and good password";
            string sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";
        
            Environment.SetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY", encryptionKeyForTest);

            var sampleEncryptedTargerURL = EncryptedFieldHelper.Encrypt(sampleTargerURL);

            // Tampering with the encryptionhey hash, this should throw a SettingsEncryptionKeyMismatchException

            try
            {
                var tamperingTest = $"{sampleEncryptedTargerURL.Substring(0, 64)}{new string (sampleEncryptedTargerURL.Substring(64, 64).Reverse().ToList().ToArray())}{sampleEncryptedTargerURL.Substring(128)}";

                var tamperedDecryption = EncryptedFieldHelper.Decrypt(tamperingTest);

                Assert.Fail("Expected SettingsEncryptionKeyMismatchException, got: " + tamperedDecryption);
            }
            catch (SettingsEncryptionKeyMismatchException)
            {
                // Expected
                Assert.True(true);
            }
            catch (Exception e)
            {
                Assert.Fail("Expected SettingsEncryptionKeyMismatchException, got: " + e);
            }

        }

        [Test]
        [Category("FieldEncryption")]
        public static void EncryptAndDecryptUsingDeviceID()
        {

            string sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";

            string encrypted = EncryptedFieldHelper.Encrypt(sampleTargerURL);

            Assert.IsNotNull(encrypted);
            Assert.IsNotEmpty(encrypted);

            string decrypted = EncryptedFieldHelper.Decrypt(encrypted);

            Assert.IsNotNull(decrypted);
            Assert.IsNotEmpty(decrypted);
            Assert.AreEqual(decrypted, sampleTargerURL);

        }

        [Test]
        [Category("FieldEncryption")]
        public static void EncryptAndDecryptUsingEnvironment()
        {

            string sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";

            Environment.SetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY", "a good and long password");

            string encrypted = EncryptedFieldHelper.Encrypt(sampleTargerURL);

            Assert.IsNotNull(encrypted);
            Assert.IsNotEmpty(encrypted);

            string decrypted = EncryptedFieldHelper.Decrypt(encrypted);

            Assert.IsNotNull(decrypted);
            Assert.IsNotEmpty(decrypted);
            Assert.AreEqual(decrypted, sampleTargerURL);

            // So far, this tests does not ensure it is using the environment variable, so lets check that
            // by changing the environment variable and checking if it still works, it should throw
            // a SettingsKeymismatchException

            Environment.SetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY", string.Empty);

            try
            {
                string secondtest = EncryptedFieldHelper.Decrypt(encrypted);

            }
            catch (SettingsEncryptionKeyMismatchException)
            {
                // Expected
                Assert.True(true);
            }
            catch (Exception e)
            {
                Assert.Fail("Expected SettingsEncryptionKeyMismatchException, got: " + e);
            }

        }

    }
}
