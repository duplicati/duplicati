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
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The web UI sends a new destination without a description, as <c>"Description": null</c>.
/// The description column is NOT NULL with an empty default, and the default only applies when
/// the column is left out, so the null was written and the request failed with a 500 (#7968).
/// </summary>
public partial class ServerApiIntegrationTests
{
    [Test]
    [Category("Integration")]
    public async Task ADestinationWithoutADescriptionCanBeAddedAndUpdated_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var baseUrl = BuildFileBackendUrl(Path.Combine(BASEFOLDER, "destination-without-description"));

            var created = await httpClient.PostAsync("/api/v2/connection-strings", Json(new JsonObject
            {
                ["Name"] = "File system #1",
                ["Description"] = null,
                ["BaseUrl"] = baseUrl
            })).ConfigureAwait(false);
            var createdBody = await created.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(created.IsSuccessStatusCode, Is.True, $"Adding the destination failed with {(int)created.StatusCode}: {createdBody}");

            var data = (JsonNode.Parse(createdBody) as JsonObject)!;
            data = GetIgnoreCase(data, "Data") as JsonObject ?? data;
            var id = GetIgnoreCase(data, "ID")!.GetValue<long>();
            Assert.That(GetIgnoreCase(data, "Description")?.GetValue<string>() ?? "", Is.Empty);

            var updated = await httpClient.PutAsync($"/api/v2/connection-string/{id}", Json(new JsonObject
            {
                ["Name"] = "File system #1 renamed",
                ["Description"] = null,
                ["BaseUrl"] = baseUrl
            })).ConfigureAwait(false);
            var updatedBody = await updated.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(updated.IsSuccessStatusCode, Is.True, $"Updating the destination failed with {(int)updated.StatusCode}: {updatedBody}");
        }).ConfigureAwait(false);
    }

    private static StringContent Json(JsonObject body)
        => new(body.ToJsonString(), Encoding.UTF8, "application/json");
}
