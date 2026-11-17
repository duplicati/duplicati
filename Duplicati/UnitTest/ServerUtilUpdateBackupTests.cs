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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.CommandLine.ServerUtil;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The update-backup command of ServerUtil, tested by the requests it sends to a stand-in for the server
/// </summary>
[TestFixture]
[Category("ServerUtil")]
public class ServerUtilUpdateBackupTests
{
    /// <summary>
    /// The configuration the stand-in server reports, with secrets masked as the server does
    /// </summary>
    private const string CurrentConfiguration = """
        {
          "Schedule": { "ID": 7, "Tags": ["ID=1"], "Time": "2026-01-01T02:00:00Z", "Repeat": "1D", "LastRun": "2026-01-01T02:00:00Z", "Rule": "", "AllowedDays": ["mon", "tue"] },
          "Backup": {
            "ID": "1",
            "Name": "Documents",
            "Description": "",
            "Tags": [],
            "TargetURL": "s3://bucket/folder?auth-password=**********",
            "Sources": ["C:\\Data\\"],
            "Settings": [
              { "Filter": "", "Name": "encryption-module", "Value": "aes", "Argument": null },
              { "Filter": "", "Name": "passphrase", "Value": "**********", "Argument": null },
              { "Filter": "", "Name": "--volume-size", "Value": "50mb", "Argument": null },
              { "Filter": "", "Name": "--list-verify-uploads", "Value": "true", "Argument": null },
              { "Filter": "*.tmp", "Name": "--volume-size", "Value": "10mb", "Argument": null }
            ],
            "Filters": [ { "Order": 0, "Include": false, "Expression": "*.bak" } ],
            "Metadata": { "LastBackupDate": "20260101T020000Z" }
          },
          "DisplayNames": { "C:\\Data\\": "Data" }
        }
        """;

    /// <summary>
    /// Stands in for the server: reports the configuration and records what is sent back
    /// </summary>
    private sealed class StubServer : HttpMessageHandler
    {
        /// <summary>
        /// The requests received, with their bodies
        /// </summary>
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method, path, body));

            if (request.Method == HttpMethod.Get && path == "/api/v1/backup/1")
                return Json(CurrentConfiguration);
            if (request.Method == HttpMethod.Put && path == "/api/v1/backup/1")
                return new HttpResponseMessage(HttpStatusCode.OK);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json)
            => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    /// <summary>
    /// Runs an update and returns the body sent with the PUT request
    /// </summary>
    /// <param name="patch">The content of the update file</param>
    /// <returns>The body the server received</returns>
    private static async Task<JsonObject> SendUpdateAsync(string patch)
    {
        var server = new StubServer();
        var connection = new Connection(new HttpClient(server) { BaseAddress = new Uri("http://localhost:8200/api/v1/") });

        var result = await connection.UpdateBackupAsync("1", BackupPatch.Parse(patch));
        Assert.AreEqual("Documents", result.Name);

        var puts = server.Requests.Where(x => x.Method == HttpMethod.Put).ToList();
        Assert.AreEqual(1, puts.Count, "Expected exactly one PUT request");
        Assert.AreEqual("/api/v1/backup/1", puts[0].Path);
        return (JsonObject)JsonNode.Parse(puts[0].Body)!;
    }

    private static Dictionary<string, string?> GlobalSettings(JsonObject body)
        => body["Backup"]!["Settings"]!.AsArray()
            .Where(x => string.IsNullOrEmpty((string?)x!["Filter"]))
            .ToDictionary(x => (string)x!["Name"]!, x => (string?)x!["Value"]);

    [Test]
    public async Task SettingsAreSetAndRemovedAndTheRestIsKept()
    {
        var body = await SendUpdateAsync("""
            {
              "Backup": {
                "Settings": {
                  "--volume-size": "80mb",
                  "--list-verify-uploads": null,
                  "--retention-policy": "1W:1D",
                  "--number-of-retries": 5
                }
              }
            }
            """);

        var settings = GlobalSettings(body);
        Assert.AreEqual("80mb", settings["--volume-size"]);
        Assert.IsFalse(settings.ContainsKey("--list-verify-uploads"), "A setting given as null must be removed");
        Assert.AreEqual("1W:1D", settings["--retention-policy"]);
        Assert.AreEqual("5", settings["--number-of-retries"]);
        Assert.AreEqual("aes", settings["encryption-module"]);
        // The masked value is sent back unchanged, so the server keeps the stored secret
        Assert.AreEqual("**********", settings["passphrase"]);

        var filtered = body["Backup"]!["Settings"]!.AsArray().Single(x => (string?)x!["Filter"] == "*.tmp");
        Assert.AreEqual("10mb", (string?)filtered!["Value"], "A setting for a filter must not be changed by a global one");

        Assert.AreEqual("s3://bucket/folder?auth-password=**********", (string?)body["Backup"]!["TargetURL"]);
        Assert.AreEqual("Documents", (string?)body["Backup"]!["Name"]);
        Assert.AreEqual("*.bak", (string?)body["Backup"]!["Filters"]![0]!["Expression"]);
        Assert.AreEqual("1D", (string?)body["Schedule"]!["Repeat"], "The schedule must be kept, or the server removes it");
        Assert.AreEqual(7, (long)body["Schedule"]!["ID"]!);
        Assert.IsNull(body["DisplayNames"]);
    }

    [Test]
    public async Task BackupAndScheduleProperties()
    {
        var body = await SendUpdateAsync("""
            {
              "backup": { "targeturl": "file:///mnt/new", "Sources": ["C:\\Other\\"] },
              "Schedule": { "Repeat": "1W" }
            }
            """);

        Assert.AreEqual("file:///mnt/new", (string?)body["Backup"]!["TargetURL"], "The name must match the existing property without regard to case");
        Assert.IsNull(body["Backup"]!["targeturl"]);
        Assert.AreEqual("C:\\Other\\", (string?)body["Backup"]!["Sources"]![0]);
        Assert.AreEqual("1W", (string?)body["Schedule"]!["Repeat"]);
        Assert.AreEqual("2026-01-01T02:00:00Z", (string?)body["Schedule"]!["Time"]);
        Assert.AreEqual("50mb", GlobalSettings(body)["--volume-size"]);
    }

    [Test]
    public async Task NullScheduleRemovesTheSchedule()
    {
        var body = await SendUpdateAsync("""{ "Schedule": null }""");

        Assert.IsTrue(body.ContainsKey("Schedule"));
        Assert.IsNull(body["Schedule"]);
        Assert.AreEqual("Documents", (string?)body["Backup"]!["Name"]);
    }

    [Test]
    public void InvalidUpdateFilesAreRejected()
    {
        NUnit.Framework.Assert.Throws<UserReportedException>(() => BackupPatch.Parse("not json"));
        NUnit.Framework.Assert.Throws<UserReportedException>(() => BackupPatch.Parse("[]"));
        NUnit.Framework.Assert.Throws<UserReportedException>(() => BackupPatch.Parse("""{ "Schedul": {} }"""));
        NUnit.Framework.Assert.Throws<UserReportedException>(() => BackupPatch.Parse("""{ "Backup": [] }"""));
        NUnit.Framework.Assert.Throws<UserReportedException>(() => BackupPatch.Parse("""{ "Schedule": "daily" }"""));

        var patch = BackupPatch.Parse("""{ "Backup": { "Settings": { "--volume-size": {} } } }""");
        NUnit.Framework.Assert.Throws<UserReportedException>(() => BackupPatch.Apply((JsonObject)JsonNode.Parse(CurrentConfiguration)!, patch));
    }
}
