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
            string sampleEncryptedTargerURL = "enc-v1:A98D36962A107AC35A6168A58C907CE726122137065B90B4849574A7D16BABA9D69C4FD28149FDDF2D73E323F021D628CC7890D00EAD12ADFA9EFBAB1DAD8D1D4145530200000039B44893CF839D92907F3B972F67718B1D77D18C3A68E6174EB50FFE476B6A4AB9E1D0A7C203D85871060740688C6DF7168879C212806E4B2C64F4AFBD142CAFE353187D8C6C14F9069400DB681EF9B1B0DA1E83C6886F46A36E586BF07F644B8898627CC426C5A86414B2F41CA075033CD7201A57EC2E393CE2EE1921DC4C96ED2A1CF651BD171BEA94F4B3BBB5ED20A85454A2FBA8B2D738555F06398086840BAFE31DCC1E73EDC88BB0B2DAE9318A3AC894722BD4183E63F40EB24C0B0D3D97F73410C4C3863244B3C983753BA7CEEBE77BF409D4E9CD1D020718D54E315A6A9B89FD5451E35BCEA5F9C006C8AE980989CF8ABBFB9E018B8CC4EAA316E5A0C20145322F18479AEB5528FBE4CAE7AB38";
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
                // Remove the prefix to tamper with the message structure
                sampleEncryptedTargerURL = sampleEncryptedTargerURL.Substring(EncryptedFieldHelper.HEADER_PREFIX.Length);

                var tamperingTest = $"{sampleEncryptedTargerURL.Substring(0, 64)}{new string (sampleEncryptedTargerURL.Substring(64, 64).Reverse().ToList().ToArray())}{sampleEncryptedTargerURL.Substring(128)}";

                // Restore the prefix, in this test, we are specifically triggering the exception by tampering the keyhash
                tamperingTest = EncryptedFieldHelper.HEADER_PREFIX + tamperingTest;

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
