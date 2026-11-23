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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A pCloud folder listing right after an upload does not always show the new file
/// yet, which made the read right after the write of the connection test fail. A file
/// uploaded through the backend is found by the ID that the upload returned.
/// </summary>
[TestFixture]
public class PCloudUploadedFileIdTests
{
    /// <summary>
    /// No path in the url, so the folder id is 0 and needs no request
    /// </summary>
    private const string Url = "pcloud://api.pcloud.com/";

    private const string Content = "the contents of a backup volume";

    /// <summary>
    /// A pCloud whose folder listing lags: it stays empty, while the upload answers
    /// with the ID of the new file
    /// </summary>
    private sealed class LaggingListingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!;
            // The backend joins the host and the call with a slash of its own, so the path starts with two
            var path = "/" + url.AbsolutePath.TrimStart('/');
            Requests.Add(path + url.Query);

            var body = path switch
            {
                "/uploadfile" => "{\"result\":0,\"fileids\":[42]}",
                "/listfolder" => "{\"result\":0,\"metadata\":{\"contents\":[]}}",
                "/getfilelink" when url.Query == "?fileid=42" => "{\"result\":0,\"hosts\":[\"download.example\"],\"path\":\"/file42\"}",
                "/getfilelink" => "{\"result\":2009}",
                "/deletefile" when url.Query == "?fileid=42" => "{\"result\":0}",
                "/deletefile" => "{\"result\":2009}",
                "/renamefile" when url.Query.StartsWith("?fileid=42&") => "{\"result\":0}",
                "/file42" => Content,
                _ => "{\"result\":1000}"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static pCloudBackend CreateBackend(LaggingListingHandler handler)
        => new(Url, new Dictionary<string, string?> { ["authid"] = "test-auth-id" }, handler);

    private static async Task PutAsync(pCloudBackend backend, string name)
    {
        await using var volume = new MemoryStream(Encoding.UTF8.GetBytes(Content));
        await backend.PutAsync(name, volume, CancellationToken.None);
    }

    private static async Task<string> GetAsync(pCloudBackend backend, string name)
    {
        using var target = new MemoryStream();
        await backend.GetAsync(name, target, CancellationToken.None);
        return Encoding.UTF8.GetString(target.ToArray());
    }

    [Test]
    [Category("Backend")]
    public async Task AFileIsReadAndDeletedRightAfterItsUploadAsync()
    {
        var handler = new LaggingListingHandler();
        using var backend = CreateBackend(handler);

        await PutAsync(backend, "duplicati-access-privileges-test-1.txt");

        Assert.AreEqual(Content, await GetAsync(backend, "duplicati-access-privileges-test-1.txt"));
        await backend.DeleteAsync("duplicati-access-privileges-test-1.txt", CancellationToken.None);

        CollectionAssert.Contains(handler.Requests, "/getfilelink?fileid=42");
        CollectionAssert.Contains(handler.Requests, "/deletefile?fileid=42");
        Assert.IsFalse(handler.Requests.Any(x => x.StartsWith("/listfolder")), "The lagging listing was used to find the file");
    }

    [Test]
    [Category("Backend")]
    public async Task ADeletedFileIsLookedUpAgainAsync()
    {
        var handler = new LaggingListingHandler();
        using var backend = CreateBackend(handler);

        await PutAsync(backend, "duplicati-b1.dblock.zip");
        await backend.DeleteAsync("duplicati-b1.dblock.zip", CancellationToken.None);

        // The ID is forgotten with the file, so the listing decides, and it has no such file
        Assert.ThrowsAsync<FileMissingException>(() => GetAsync(backend, "duplicati-b1.dblock.zip"));
        Assert.IsTrue(handler.Requests.Any(x => x.StartsWith("/listfolder")));
    }

    [Test]
    [Category("Backend")]
    public async Task ARenamedFileKeepsItsIdAsync()
    {
        var handler = new LaggingListingHandler();
        using var backend = CreateBackend(handler);

        await PutAsync(backend, "duplicati-b1.dblock.zip");
        await backend.RenameAsync("duplicati-b1.dblock.zip", "duplicati-b2.dblock.zip", CancellationToken.None);

        Assert.AreEqual(Content, await GetAsync(backend, "duplicati-b2.dblock.zip"));
        Assert.ThrowsAsync<FileMissingException>(() => GetAsync(backend, "duplicati-b1.dblock.zip"));
    }
}
