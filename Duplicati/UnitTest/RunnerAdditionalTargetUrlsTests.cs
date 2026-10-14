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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Duplicati;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using Backup = Duplicati.Server.Database.Backup;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The remote control storage credentials are never stored in a backup's target url; the server
    /// merges them in when the backup runs. These tests cover that the additional targets get the
    /// same treatment as the primary target.
    /// </summary>
    public class RunnerAdditionalTargetUrlsTests : BasicSetupHelper
    {
        private const string API_ID = "storage-api-id";
        private const string API_KEY = "storage-api-key";
        private const string ENDPOINT = "https://storage.example.com/";

        private string serverDatafolder = "";

        [OneTimeSetUp]
        public void Setup()
        {
            serverDatafolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(serverDatafolder);
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(serverDatafolder, true);
            }
            catch
            {
                // If there's an exception, let the OS deal with cleaning up the temp folder.
            }
        }

        private async Task<Connection> CreateConnectionAsync()
        {
            var dbpath = Path.Combine(serverDatafolder, Path.GetRandomFileName());
            var con = await SQLiteLoader.LoadConnectionAsync().ConfigureAwait(false);
            await SQLiteLoader.OpenDatabaseAsync(con, dbpath, null).ConfigureAwait(false);
            DatabaseUpgrader.UpgradeDatabase(con, dbpath, typeof(Library.RestAPI.Database.DatabaseSchemaMarker));

            var connection = new Connection(con, true, null, serverDatafolder, () => { });
            connection.ApplicationSettings.RemoteControlStorageApiId = API_ID;
            connection.ApplicationSettings.RemoteControlStorageApiKey = API_KEY;
            connection.ApplicationSettings.RemoteControlStorageEndpointUrl = ENDPOINT;
            return connection;
        }

        private static List<string> MappedDestinationUrls(Dictionary<string, string?> options)
        {
            using var doc = JsonDocument.Parse(options["remote-sync-json-config"]!);
            return doc.RootElement.GetProperty("destinations").EnumerateArray()
                .Select(x => x.GetProperty("url").GetString()!)
                .ToList();
        }

        [Test]
        [Category("RemoteSync")]
        public async Task AdditionalDuplicatiTargetGetsStorageCredentials()
        {
            using var connection = await CreateConnectionAsync().ConfigureAwait(false);
            var backup = new Backup
            {
                TargetURL = $"file://{Path.Combine(serverDatafolder, "primary")}",
                AdditionalTargetURLs =
                [
                    new TargetUrlEntry { TargetUrl = $"{DuplicatiBackend.PROTOCOL}://", Mode = "inline" }
                ]
            };

            var options = new Dictionary<string, string?>();
            Runner.ApplyAdditionalTargetUrls(connection, backup, options);

            var urls = MappedDestinationUrls(options);
            Assert.AreEqual(1, urls.Count);

            var uri = new Library.Utility.RelaxedUri(urls[0]);
            var query = uri.GetEncodedQueryParameters();
            Assert.AreEqual(DuplicatiBackend.PROTOCOL, uri.Scheme);
            Assert.AreEqual("storage.example.com", uri.Host);
            Assert.AreEqual(API_ID, Library.Utility.UrlEncoding.UrlDecode(query.Get(DuplicatiBackend.AUTH_API_ID_OPTION)!));
            Assert.AreEqual(API_KEY, Library.Utility.UrlEncoding.UrlDecode(query.Get(DuplicatiBackend.AUTH_API_KEY_OPTION)!));
        }

        [Test]
        [Category("RemoteSync")]
        public async Task AdditionalDuplicatiTargetKeepsItsOwnCredentials()
        {
            using var connection = await CreateConnectionAsync().ConfigureAwait(false);
            var ownUrl = $"{DuplicatiBackend.PROTOCOL}://own.example.com/folder?{DuplicatiBackend.AUTH_API_ID_OPTION}=own-id&{DuplicatiBackend.AUTH_API_KEY_OPTION}=own-key";
            var backup = new Backup
            {
                TargetURL = $"file://{Path.Combine(serverDatafolder, "primary")}",
                AdditionalTargetURLs = [new TargetUrlEntry { TargetUrl = ownUrl, Mode = "inline" }]
            };

            var options = new Dictionary<string, string?>();
            Runner.ApplyAdditionalTargetUrls(connection, backup, options);

            var uri = new Library.Utility.RelaxedUri(MappedDestinationUrls(options).Single());
            var query = uri.GetEncodedQueryParameters();
            Assert.AreEqual("own.example.com", uri.Host);
            Assert.AreEqual("own-id", query.Get(DuplicatiBackend.AUTH_API_ID_OPTION));
            Assert.AreEqual("own-key", query.Get(DuplicatiBackend.AUTH_API_KEY_OPTION));
        }

        [Test]
        [Category("RemoteSync")]
        public async Task AdditionalNonDuplicatiTargetIsUnchanged()
        {
            using var connection = await CreateConnectionAsync().ConfigureAwait(false);
            var fileUrl = $"file://{Path.Combine(serverDatafolder, "secondary")}";
            var backup = new Backup
            {
                TargetURL = $"file://{Path.Combine(serverDatafolder, "primary")}",
                AdditionalTargetURLs = [new TargetUrlEntry { TargetUrl = fileUrl, Mode = "inline" }]
            };

            var options = new Dictionary<string, string?>();
            Runner.ApplyAdditionalTargetUrls(connection, backup, options);

            var url = MappedDestinationUrls(options).Single();
            Assert.AreEqual(fileUrl, url);
            StringAssert.DoesNotContain(API_KEY, url);
        }
    }
}
