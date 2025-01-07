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
using System.Linq;
using Duplicati.Library.Encryption;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
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
            var encryptionKeyForTest = "long and good password";
            var sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";
            var sampleEncryptedTargerURL = "enc-v1:2F9E5DFE5824792C31843AF6B242C40414D1B671B36E70361CF9DB8B0F502310138E362F5B564379CDE40F73BC96D4EAB0B949CC82A592D0194040FA08DD49B241455302000000F9B39F7AF68292D631E05A254E433C2F416D68B57C8DD633B94EBB452A7275585EDC7D71CC5187B083E49FDF9E927C10E6FEA7C925A0BA4E83C7CFC985FD925B011A5AB863532EE6877BE79B6BAA6E0871917822B4DB7456135F982D2E80B94C236CEA5AB41F55D32B4B0AA4981607E2E939A45CF80F3E38603AB1CB8127196F5DB1C869B575BC651D9E2840E5551A9178526BCCDC82867171CD40527FB443E8727B8EBB13F70A9C415D80F8AC649A12C075376F632C4A3408ACEFC7D5C14EBB0D4F91E0AC9DE00A33E42E62B1E03CBE5D1CB5F79609F5CCE4872D8F414485BED8C40DFCE4A3423A09996A477AEB1DB709A437F43FA272B416A8309F0A76C9552CC5BE2C501AC7BB427ABFBF60ADEFA2C7";

            var key = EncryptedFieldHelper.KeyInstance.CreateKey(encryptionKeyForTest);

            // Sample URL is not encrypted, so it should not suffer transformation and be returned as is
            Assert.AreEqual(EncryptedFieldHelper.Decrypt(sampleTargerURL, key), sampleTargerURL);

            // SampleEncrypted URL is encrypted, so it should be decrypted and returned matching the unencrypted version
            Assert.AreEqual(EncryptedFieldHelper.Decrypt(sampleEncryptedTargerURL, key), sampleTargerURL);

        }

        [Test]
        [Category("FieldEncryption")]
        public static void TestTamperingFirstHash()
        {

            var encryptionKeyForTest = "long and good password";
            var sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";

            var key = EncryptedFieldHelper.KeyInstance.CreateKey(encryptionKeyForTest);

            var sampleEncryptedTargerURL = EncryptedFieldHelper.Encrypt(sampleTargerURL, key);
            // Tampering now with the first bytes of encrypted string, which is the content hash,

            var tamperingTest1 = $"{sampleEncryptedTargerURL.Substring(0, 64).Reverse()}{sampleEncryptedTargerURL.Substring(64)}";

            var tamperedDecryption = EncryptedFieldHelper.Decrypt(tamperingTest1, key);

            // Because the hash is tampered, the EncryptedFieldHelper will not perceive the record as a valid encrypted field, and will return
            // as is, so the returned value should be equal to the tampered value

            Assert.AreEqual(tamperedDecryption, tamperingTest1);

        }

        [Test]
        [Category("FieldEncryption")]
        public static void TestTamperingKeyHash()
        {
            var encryptionKeyForTest = "long and good password";
            var sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";

            var key = EncryptedFieldHelper.KeyInstance.CreateKey(encryptionKeyForTest);

            var sampleEncryptedTargerURL = EncryptedFieldHelper.Encrypt(sampleTargerURL, key);

            // Tampering with the encryptionhey hash, this should throw a SettingsEncryptionKeyMismatchException

            try
            {
                // Remove the prefix to tamper with the message structure
                sampleEncryptedTargerURL = sampleEncryptedTargerURL.Substring(EncryptedFieldHelper.HEADER_PREFIX.Length);

                var tamperingTest = $"{sampleEncryptedTargerURL.Substring(0, 64)}{new string(sampleEncryptedTargerURL.Substring(64, 64).Reverse().ToList().ToArray())}{sampleEncryptedTargerURL.Substring(128)}";

                // Restore the prefix, in this test, we are specifically triggering the exception by tampering the keyhash
                tamperingTest = EncryptedFieldHelper.HEADER_PREFIX + tamperingTest;

                var tamperedDecryption = EncryptedFieldHelper.Decrypt(tamperingTest, key);

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
        public static void TestBlacklistedKeysCannotEncrypt()
        {
            using var hasher = HashFactory.CreateHasher("SHA256"); ;
            if (!EncryptedFieldHelper.IsKeyBlacklisted("".ComputeHashToHex(hasher)))
                throw new Exception("Expected empty string hash to be blacklisted");

            var key = EncryptedFieldHelper.KeyInstance.CreateKey("ECB47E9D8445E0A3F30A1435BE075C101F202FF5445BA01A9F9A8DBD4506F5F3");
            if (!key.IsBlacklisted)
                throw new Exception("Expected empty key to be blacklisted");

            Assert.Throws<InvalidOperationException>(() => EncryptedFieldHelper.Encrypt("test", key));
        }


        [Test]
        [Category("FieldEncryption")]
        public static void EncryptAndDecryptUsingDeviceID()
        {
            // If there is no trusted device ID, this test cannot be performed
            if (!DeviceIDHelper.HasTrustedDeviceID)
                return;

            var sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";
            var key = EncryptedFieldHelper.KeyInstance.CreateKey(DeviceIDHelper.GetDeviceIDHash());

            // If the key is blacklisted, it cannot be tested
            if (key.IsBlacklisted)
                return;
            var encrypted = EncryptedFieldHelper.Encrypt(sampleTargerURL, key);

            Assert.IsNotNull(encrypted);
            Assert.IsNotEmpty(encrypted);

            var decrypted = EncryptedFieldHelper.Decrypt(encrypted, key);

            Assert.IsNotNull(decrypted);
            Assert.IsNotEmpty(decrypted);
            Assert.AreEqual(decrypted, sampleTargerURL);

        }

        [Test]
        [Category("FieldEncryption")]
        public static void EncryptAndDecryptUsingCustomKey()
        {

            var sampleTargerURL = "s3://awsid-bucket/folder/?s3-location-constraint=us-east-2&s3-storage-class=&s3-client=aws&auth-username=AWSID&auth-password=AWSACCESSKEY";

            var key = EncryptedFieldHelper.KeyInstance.CreateKey("a good and long password");

            var encrypted = EncryptedFieldHelper.Encrypt(sampleTargerURL, key);

            Assert.IsNotNull(encrypted);
            Assert.IsNotEmpty(encrypted);

            var decrypted = EncryptedFieldHelper.Decrypt(encrypted, key);

            Assert.IsNotNull(decrypted);
            Assert.IsNotEmpty(decrypted);
            Assert.AreEqual(decrypted, sampleTargerURL);

            try
            {
                // So far, this tests does not ensure it is using the any key, so lets check that
                // by using the differnt key and checking if it still works, it should throw
                // a SettingsKeymismatchException
                var secondtest = EncryptedFieldHelper.Decrypt(encrypted, EncryptedFieldHelper.KeyInstance.CreateKey("another good and long password"));

            }
            catch (Exception ex)
                when (ex is SettingsEncryptionKeyMismatchException || ex is SettingsEncryptionKeyMissingException)
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
