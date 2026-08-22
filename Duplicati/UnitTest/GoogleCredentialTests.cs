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

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The Google Cloud Storage options are documented as taking the JSON key of a service
    /// account, and nothing else. These pin that: a service account is accepted, and a
    /// credential of another kind is not.
    ///
    /// Nothing here talks to Google. Reading a credential parses the JSON and the key and
    /// stops there, and the http client the backend builds from it only asks for a token when
    /// it sends a request, so a key that was generated here and registered nowhere is enough.
    /// </summary>
    [TestFixture]
    public class GoogleCredentialTests
    {
        private const string URL = "googlecloudstorage://a-bucket/a-prefix";
        private const string JSON_OPTION = "gcs-service-account-json";
        private const string FILE_OPTION = "gcs-service-account-file";

        /// <summary>
        /// A service account key in the shape Google hands out, with a key made here. It is
        /// structurally a real key and registered with nobody, which is all that reading it
        /// requires.
        /// </summary>
        private static string ServiceAccountJson()
        {
            using var rsa = RSA.Create(2048);
            return JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["type"] = "service_account",
                ["project_id"] = "duplicati-unit-test",
                ["private_key_id"] = "0000000000000000000000000000000000000000",
                ["private_key"] = rsa.ExportPkcs8PrivateKeyPem() + "\n",
                ["client_email"] = "unit-test@duplicati-unit-test.iam.gserviceaccount.com",
                ["client_id"] = "000000000000000000000",
                ["token_uri"] = "https://oauth2.googleapis.com/token"
            });
        }

        /// <summary>
        /// The other kind the old loader accepted: a user credential as the Cloud SDK stores
        /// it. The options have never been documented as taking one.
        /// </summary>
        private static string UserCredentialJson()
            => JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["type"] = "authorized_user",
                ["client_id"] = "000000000000-duplicati.apps.googleusercontent.com",
                ["client_secret"] = "not-a-real-secret",
                ["refresh_token"] = "not-a-real-token"
            });

        [Test]
        [Category("GoogleCredentials")]
        public void AServiceAccountKeyIsAccepted()
        {
            var options = new Dictionary<string, string?> { [JSON_OPTION] = ServiceAccountJson() };
            Assert.DoesNotThrow(
                () => new Library.Backend.GoogleCloudStorage.GoogleCloudStorage(URL, options),
                "A service account key is what these options are documented to take");
        }

        [Test]
        [Category("GoogleCredentials")]
        public void AServiceAccountKeyInAFileIsAccepted()
        {
            var path = Path.Combine(Path.GetTempPath(), $"duplicati-gcs-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, ServiceAccountJson());
            try
            {
                var options = new Dictionary<string, string?> { [FILE_OPTION] = path };
                Assert.DoesNotThrow(
                    () => new Library.Backend.GoogleCloudStorage.GoogleCloudStorage(URL, options),
                    "The file option has to accept exactly what the inline option accepts");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        [Category("GoogleCredentials")]
        public void AUserCredentialIsRejected()
        {
            var options = new Dictionary<string, string?> { [JSON_OPTION] = UserCredentialJson() };
            Assert.Catch(
                () => new Library.Backend.GoogleCloudStorage.GoogleCloudStorage(URL, options),
                "A credential that is not a service account has to be refused rather than used: "
                + "the option is documented as taking a service account, and accepting whatever "
                + "arrives is how an unvalidated credential configuration gets through");
        }
    }
}
