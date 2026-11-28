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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.GoogleCloudStorage;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The Cloud Storage backend reports objects in archive storage classes as archived, so
/// verification leaves them alone as it does for S3 and Azure. Cloud Storage can read every
/// class directly, but a read costs a retrieval fee and, under Autoclass, moves the object
/// back to Standard storage.
///
/// Nothing here talks to Google: the listing is answered by a stub handler.
/// </summary>
[TestFixture]
public class GoogleCloudStorageArchiveClassTests
{
    /// <summary>
    /// One object in each storage class, listed under the backend's prefix
    /// </summary>
    private const string ListBody = """
        {"items":[
          {"name":"prefix/standard.zip","size":"10","updated":"2026-01-01T00:00:00Z","storageClass":"STANDARD"},
          {"name":"prefix/nearline.zip","size":"10","updated":"2026-01-01T00:00:00Z","storageClass":"NEARLINE"},
          {"name":"prefix/coldline.zip","size":"10","updated":"2026-01-01T00:00:00Z","storageClass":"COLDLINE"},
          {"name":"prefix/archive.zip","size":"10","updated":"2026-01-01T00:00:00Z","storageClass":"ARCHIVE"},
          {"name":"prefix/unknown.zip","size":"10","updated":"2026-01-01T00:00:00Z"}
        ]}
        """;

    /// <summary>
    /// Answers every request with the listing
    /// </summary>
    private sealed class ListHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ListBody, Encoding.UTF8, "application/json")
            });
    }

    /// <summary>
    /// Lists the stub bucket and returns which names were reported as archived
    /// </summary>
    /// <param name="archiveClasses">The archive classes option, or <c>null</c> to leave it out</param>
    private static async Task<Dictionary<string, bool>> ListArchivedAsync(string? archiveClasses)
    {
        var options = new Dictionary<string, string?> { ["gcs-access-token"] = "test-token" };
        if (archiveClasses != null)
            options["gcs-archive-classes"] = archiveClasses;

        var backend = new GoogleCloudStorage("gcs://bucket/prefix", options, new HttpClient(new ListHandler()));
        var entries = await backend.ListAsync(CancellationToken.None).ToListAsync().ConfigureAwait(false);
        return entries.ToDictionary(e => e.Name, e => e.IsArchived);
    }

    [Test]
    public async Task List_ByDefault_ReportsColdlineAndArchiveAsArchived()
    {
        var archived = await ListArchivedAsync(null).ConfigureAwait(false);

        Assert.AreEqual(5, archived.Count);
        Assert.IsFalse(archived["standard.zip"]);
        Assert.IsFalse(archived["nearline.zip"]);
        Assert.IsTrue(archived["coldline.zip"]);
        Assert.IsTrue(archived["archive.zip"]);
        Assert.IsFalse(archived["unknown.zip"], "An object without a storage class is not archived");
    }

    [Test]
    public async Task List_WithArchiveClassesOption_UsesTheGivenClassesWithoutCase()
    {
        var archived = await ListArchivedAsync(" nearline , Archive ").ConfigureAwait(false);

        Assert.IsFalse(archived["standard.zip"]);
        Assert.IsTrue(archived["nearline.zip"]);
        Assert.IsFalse(archived["coldline.zip"]);
        Assert.IsTrue(archived["archive.zip"]);
    }

    [Test]
    public void SupportedCommands_ListsTheArchiveClassesOption()
    {
        var option = new GoogleCloudStorage().SupportedCommands.Single(x => x.Name == "gcs-archive-classes");

        Assert.AreEqual("COLDLINE,ARCHIVE", option.DefaultValue);
        CollectionAssert.IsSupersetOf(option.ValidValues, new[] { "STANDARD", "NEARLINE", "COLDLINE", "ARCHIVE" });
    }
}
