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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Duplicati.WebserverCore.Dto;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Saves a job back to <c>PUT /api/v1/backup/{id}</c> the way the web clients do, and checks that
/// nothing is lost. The fields come from <see cref="BackupAndScheduleInputDto"/> by reflection, so a
/// new field is covered without changing this test, and fails until its entry is added to
/// <see cref="OmittedFieldContract"/>.
/// </summary>
public partial class ServerApiIntegrationTests
{
    /// <summary>
    /// What the server must do with a field that a PUT leaves out
    /// </summary>
    private enum OmittedField
    {
        /// <summary>The job keeps its current value</summary>
        Keep,
        /// <summary>The value is cleared or reset to its default</summary>
        Clear,
        /// <summary>The request is refused</summary>
        Reject
    }

    /// <summary>
    /// How each field of the PUT body must be treated when a client leaves it out. A client that
    /// changes one setting, like the warning suppression in issue #7850, sends only the fields it
    /// knows, so a field that is cleared when it is left out is lost.
    /// </summary>
    private static readonly Dictionary<string, OmittedField> OmittedFieldContract = new(StringComparer.Ordinal)
    {
        ["Backup.Name"] = OmittedField.Keep,
        ["Backup.Description"] = OmittedField.Keep,
        ["Backup.DBPath"] = OmittedField.Keep,
        ["Backup.Tags"] = OmittedField.Keep,
        ["Backup.TargetURL"] = OmittedField.Keep,
        ["Backup.ConnectionStringID"] = OmittedField.Keep,
        ["Backup.OperationType"] = OmittedField.Keep,
        ["Backup.Enabled"] = OmittedField.Keep,
        ["Backup.RunAfterBackupID"] = OmittedField.Keep,
        ["Backup.RunAfterCondition"] = OmittedField.Keep,
        ["Backup.Sources"] = OmittedField.Keep,
        ["Backup.Settings"] = OmittedField.Keep,
        ["Backup.Filters"] = OmittedField.Keep,
        ["Backup.Metadata"] = OmittedField.Keep,
        ["Backup.AdditionalTargetURLs"] = OmittedField.Keep,
        ["Schedule"] = OmittedField.Keep,
        ["Schedule.ID"] = OmittedField.Keep,
        ["Schedule.Tags"] = OmittedField.Keep,
        ["Schedule.Time"] = OmittedField.Keep,
        ["Schedule.Repeat"] = OmittedField.Keep,
        ["Schedule.LastRun"] = OmittedField.Keep,
        ["Schedule.Rule"] = OmittedField.Keep,
        ["Schedule.AllowedDays"] = OmittedField.Keep,
    };

    /// <summary>
    /// The fields of the PUT body, as "Backup.Name", "Schedule" and "Schedule.Repeat". A property
    /// the JSON reader ignores, like HasRunAfterBackupID, is not a field a client can send.
    /// </summary>
    private static string[] InputFieldPaths()
    {
        static IEnumerable<string> Fields(Type type, string prefix)
            => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() == null)
                .Select(p => $"{prefix}.{p.Name}");

        return Fields(typeof(BackupAndScheduleInputDto.BackupInputDto), "Backup")
            .Append("Schedule")
            .Concat(Fields(typeof(BackupAndScheduleInputDto.ScheduleInputDto), "Schedule"))
            .ToArray();
    }

    /// <summary>
    /// Finds a property by name, ignoring case, as the server reads the body
    /// </summary>
    private static JsonNode? GetIgnoreCase(JsonObject? obj, string name)
        => obj?.FirstOrDefault(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>
    /// Reads the value of a field path from a GET response
    /// </summary>
    private static JsonNode? GetField(JsonObject getResponse, string path)
    {
        var parts = path.Split('.');
        var section = GetIgnoreCase(getResponse, parts[0]) as JsonObject;
        return parts.Length == 1 ? section : GetIgnoreCase(section, parts[1]);
    }

    /// <summary>
    /// Builds a PUT body from a GET response with every input field, except the one left out
    /// </summary>
    private static JsonObject ToInputBody(JsonObject getResponse, string? omit = null)
    {
        JsonObject Section(string name)
        {
            var section = new JsonObject();
            var source = GetIgnoreCase(getResponse, name) as JsonObject;
            foreach (var path in InputFieldPaths().Where(x => x.StartsWith(name + ".", StringComparison.Ordinal)))
            {
                var field = path[(name.Length + 1)..];
                if (path != omit && source != null && source.Any(x => string.Equals(x.Key, field, StringComparison.OrdinalIgnoreCase)))
                    section[field] = GetIgnoreCase(source, field)?.DeepClone();
            }
            return section;
        }

        var body = new JsonObject { ["Backup"] = Section("Backup") };
        if (omit != "Schedule" && GetIgnoreCase(getResponse, "Schedule") is JsonObject)
            body["Schedule"] = Section("Schedule");
        return body;
    }

    private static async Task<JsonObject> GetJobAsync(HttpClient httpClient, string id)
        => (JsonNode.Parse(await httpClient.GetStringAsync($"/api/v1/backup/{id}").ConfigureAwait(false)) as JsonObject)
           ?? throw new InvalidOperationException("Backup get response was not an object");

    private static Task<HttpResponseMessage> PutJobAsync(HttpClient httpClient, string id, JsonObject body)
        => httpClient.PutAsync($"/api/v1/backup/{id}", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));

    /// <summary>
    /// Creates a job where every field has a value other than its default
    /// </summary>
    private async Task<string> CreateRoundTripJobAsync(HttpClient httpClient)
    {
        var connectionResponse = await httpClient.PostAsync("/api/v2/connection-strings", new StringContent(new JsonObject
        {
            ["Name"] = $"Round trip connection {Guid.NewGuid():N}",
            ["Description"] = "Round trip connection",
            ["BaseUrl"] = BuildFileBackendUrl(Path.Combine(BASEFOLDER, "connection-target"))
        }.ToJsonString(), Encoding.UTF8, "application/json")).ConfigureAwait(false);
        connectionResponse.EnsureSuccessStatusCode();
        var connection = JsonNode.Parse(await connectionResponse.Content.ReadAsStringAsync().ConfigureAwait(false)) as JsonObject;
        var connectionId = GetIgnoreCase(GetIgnoreCase(connection, "Data") as JsonObject ?? connection, "ID")?.GetValue<long>()
                           ?? throw new InvalidOperationException("No connection string ID in the response");

        // Another job for RunAfterBackupID to point at
        var runAfterId = await CreateBackupAsync(httpClient, "round-trip-passphrase", $"Round trip run after {Guid.NewGuid():N}", Path.Combine(BASEFOLDER, "run-after-target")).ConfigureAwait(false);

        Directory.CreateDirectory(this.TARGETFOLDER);
        var body = new JsonObject
        {
            ["Backup"] = new JsonObject
            {
                ["Name"] = $"Round trip job {Guid.NewGuid():N}",
                ["Description"] = "Round trip description",
                ["DBPath"] = Path.Combine(BASEFOLDER, "round-trip.sqlite"),
                ["Tags"] = new JsonArray("alpha", "beta"),
                ["TargetURL"] = BuildFileBackendUrl(this.TARGETFOLDER),
                ["ConnectionStringID"] = connectionId,
                ["OperationType"] = "Sync",
                ["Enabled"] = false,
                ["RunAfterBackupID"] = runAfterId,
                ["RunAfterCondition"] = "Always",
                ["Sources"] = new JsonArray(this.DATAFOLDER),
                ["Settings"] = new JsonArray(
                    new JsonObject { ["Name"] = "--no-encryption", ["Value"] = "true", ["Filter"] = "" },
                    new JsonObject { ["Name"] = "--number-of-retries", ["Value"] = "7", ["Filter"] = "" }),
                ["Filters"] = new JsonArray(new JsonObject { ["Order"] = 0, ["Include"] = false, ["Expression"] = "*.tmp" }),
                ["Metadata"] = new JsonObject { ["RoundTripKey"] = "RoundTripValue" },
                ["AdditionalTargetURLs"] = new JsonArray(new JsonObject
                {
                    ["UrlKey"] = "round-trip-extra",
                    ["TargetUrl"] = BuildFileBackendUrl(Path.Combine(BASEFOLDER, "extra-target")),
                    ["Mode"] = "inline",
                    ["Interval"] = "1W",
                    ["ConnectionStringID"] = -1,
                    ["Options"] = new JsonObject()
                })
            },
            ["Schedule"] = new JsonObject
            {
                ["Tags"] = new JsonArray("round-trip"),
                ["Time"] = "2030-01-02T03:04:05Z",
                ["Repeat"] = "2D",
                ["LastRun"] = "2026-01-02T03:04:05Z",
                ["Rule"] = "AllowedWeekDays=Monday,Friday",
                ["AllowedDays"] = new JsonArray("Monday", "Friday")
            }
        };

        var response = await httpClient.PostAsync("/api/v1/backups", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            Assert.Fail($"Creating the round trip job failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        var created = await response.Content.ReadFromJsonAsync<CreateBackupDto>(JsonOptions).ConfigureAwait(false);
        return created?.ID ?? throw new InvalidOperationException("No backup ID in the response");
    }

    private static string Show(JsonNode? node) => node?.ToJsonString() ?? "(missing)";

    [Test]
    [Category("Integration")]
    public async Task SavingAJobAsItWasReadKeepsEveryField_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var id = await CreateRoundTripJobAsync(httpClient).ConfigureAwait(false);
            var before = await GetJobAsync(httpClient, id).ConfigureAwait(false);

            var response = await PutJobAsync(httpClient, id, ToInputBody(before)).ConfigureAwait(false);
            Assert.That(response.IsSuccessStatusCode, Is.True, $"Saving the job as it was read failed: {await response.Content.ReadAsStringAsync().ConfigureAwait(false)}");
            var after = await GetJobAsync(httpClient, id).ConfigureAwait(false);

            var changed = InputFieldPaths()
                .Where(path => !JsonNode.DeepEquals(GetField(before, path), GetField(after, path)))
                .Select(path => $"{path}: {Show(GetField(before, path))} -> {Show(GetField(after, path))}")
                .ToList();
            Assert.That(changed, Is.Empty, "Saving a job as it was read changed:\n" + string.Join("\n", changed));
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task SavingAJobWithoutAFieldTreatsTheFieldAsAgreed_Async()
    {
        var unknown = InputFieldPaths().Where(path => !OmittedFieldContract.ContainsKey(path)).ToList();
        Assert.That(unknown, Is.Empty, "Decide in OmittedFieldContract what a PUT without these fields does:\n" + string.Join("\n", unknown));

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var id = await CreateRoundTripJobAsync(httpClient).ConfigureAwait(false);
            var original = ToInputBody(await GetJobAsync(httpClient, id).ConfigureAwait(false));
            var results = new List<string>();
            var mismatches = new List<string>();

            foreach (var path in InputFieldPaths())
            {
                // Start from the full job each time, since a field that is lost stays lost
                (await PutJobAsync(httpClient, id, original.DeepClone().AsObject()).ConfigureAwait(false)).EnsureSuccessStatusCode();
                var baseline = await GetJobAsync(httpClient, id).ConfigureAwait(false);

                var response = await PutJobAsync(httpClient, id, ToInputBody(baseline, path)).ConfigureAwait(false);
                OmittedField observed;
                string detail;
                if (!response.IsSuccessStatusCode)
                {
                    observed = OmittedField.Reject;
                    detail = $"refused with {(int)response.StatusCode}";
                }
                else
                {
                    var after = await GetJobAsync(httpClient, id).ConfigureAwait(false);
                    var was = GetField(baseline, path);
                    var now = GetField(after, path);
                    observed = JsonNode.DeepEquals(was, now) ? OmittedField.Keep : OmittedField.Clear;
                    detail = observed == OmittedField.Keep ? "kept" : $"{Show(was)} -> {Show(now)}";
                }

                var expected = OmittedFieldContract[path];
                var line = $"{path}: expected {expected}, got {observed} ({detail})";
                results.Add(line);
                if (observed != expected)
                    mismatches.Add(line);
            }

            TestContext.Progress.WriteLine("PUT without a field:\n" + string.Join("\n", results));
            Assert.That(mismatches, Is.Empty, "A PUT without these fields did not do what was agreed:\n" + string.Join("\n", mismatches));
        }).ConfigureAwait(false);
    }
}
