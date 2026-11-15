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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Duplicati.WebserverCore.Dto;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A client that changes one setting of a job may send only the fields it knows. The fields it
/// leaves out must keep their values, and fields it sends empty must still be cleared.
/// </summary>
public partial class ServerApiIntegrationTests
{
    [Test]
    [Category("Integration")]
    public async Task SavingAJobWithoutOptionalFieldsKeepsThem_Async()
    {
        var name = $"Omitted fields job {Guid.NewGuid():N}";
        Directory.CreateDirectory(this.TARGETFOLDER);

        JsonObject Body(bool withOptionalFields, bool emptyOptionalFields = false)
        {
            var backup = new JsonObject
            {
                ["Name"] = name,
                ["TargetURL"] = BuildFileBackendUrl(this.TARGETFOLDER),
                ["Sources"] = new JsonArray(this.DATAFOLDER),
                ["Settings"] = new JsonArray(new JsonObject { ["Name"] = "--no-encryption", ["Value"] = "true", ["Filter"] = "" })
            };
            var schedule = new JsonObject { ["Repeat"] = "2D", ["Rule"] = "" };
            if (withOptionalFields)
            {
                backup["Description"] = emptyOptionalFields ? "" : "Kept description";
                backup["Tags"] = emptyOptionalFields ? new JsonArray() : new JsonArray("alpha", "beta");
                backup["Filters"] = emptyOptionalFields ? new JsonArray() : new JsonArray(new JsonObject { ["Order"] = 0, ["Include"] = false, ["Expression"] = "*.tmp" });
                backup["AdditionalTargetURLs"] = emptyOptionalFields ? new JsonArray() : new JsonArray(new JsonObject
                {
                    ["UrlKey"] = "kept-extra",
                    ["TargetUrl"] = BuildFileBackendUrl(Path.Combine(BASEFOLDER, "extra-target")),
                    ["Mode"] = "inline",
                    ["Interval"] = "1W"
                });
                schedule["Time"] = "2030-01-02T03:04:05Z";
                schedule["AllowedDays"] = emptyOptionalFields ? new JsonArray() : new JsonArray("Monday", "Friday");
            }
            return new JsonObject { ["Backup"] = backup, ["Schedule"] = schedule };
        }

        static StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            // Read as JSON: the schedule days come back as "mon" and "fri", which DayOfWeek does not parse
            async Task<(JsonObject Backup, JsonObject? Schedule)> GetAsync(string id)
            {
                var root = JsonNode.Parse(await httpClient.GetStringAsync($"/api/v1/backup/{id}").ConfigureAwait(false))?.AsObject()
                           ?? throw new InvalidOperationException("Backup get response was empty");
                return (root["Backup"]!.AsObject(), root["Schedule"] as JsonObject);
            }
            static string[] Strings(JsonNode? node) => node?.AsArray().Select(x => x!.ToString()).ToArray() ?? [];

            var createResponse = await httpClient.PostAsync("/api/v1/backups", Json(Body(true))).ConfigureAwait(false);
            createResponse.EnsureSuccessStatusCode();
            var id = (await createResponse.Content.ReadFromJsonAsync<CreateBackupDto>(JsonOptions).ConfigureAwait(false))?.ID
                     ?? throw new InvalidOperationException("No backup ID in the response");

            (await httpClient.PutAsync($"/api/v1/backup/{id}", Json(Body(false))).ConfigureAwait(false)).EnsureSuccessStatusCode();
            var kept = await GetAsync(id).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(kept.Backup["Description"]?.ToString(), Is.EqualTo("Kept description"), "Description");
                Assert.That(Strings(kept.Backup["Tags"]), Is.EqualTo(new[] { "alpha", "beta" }), "Tags");
                Assert.That(kept.Backup["Filters"]?.AsArray().Select(x => x!["Expression"]?.ToString()), Is.EqualTo(new[] { "*.tmp" }), "Filters");
                Assert.That(kept.Backup["AdditionalTargetURLs"]?.AsArray().Select(x => x!["UrlKey"]?.ToString()), Is.EqualTo(new[] { "kept-extra" }), "AdditionalTargetURLs");
                Assert.That(kept.Schedule?["Time"]?.GetValue<DateTime>().ToUniversalTime(), Is.EqualTo(new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc)), "Schedule.Time");
                Assert.That(Strings(kept.Schedule?["AllowedDays"]), Is.EquivalentTo(new[] { "mon", "fri" }), "Schedule.AllowedDays");
            });

            (await httpClient.PutAsync($"/api/v1/backup/{id}", Json(Body(true, emptyOptionalFields: true))).ConfigureAwait(false)).EnsureSuccessStatusCode();
            var cleared = await GetAsync(id).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(cleared.Backup["Description"]?.ToString(), Is.Empty, "Description sent empty");
                Assert.That(Strings(cleared.Backup["Tags"]), Is.Empty, "Tags sent empty");
                Assert.That(cleared.Backup["Filters"]?.AsArray(), Is.Empty, "Filters sent empty");
                Assert.That(cleared.Backup["AdditionalTargetURLs"]?.AsArray(), Is.Empty, "AdditionalTargetURLs sent empty");
                Assert.That(Strings(cleared.Schedule?["AllowedDays"]), Is.Empty, "Schedule.AllowedDays sent empty");
            });
        }).ConfigureAwait(false);
    }
}
