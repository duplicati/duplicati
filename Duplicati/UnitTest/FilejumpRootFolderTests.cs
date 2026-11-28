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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A Filejump destination at the root of the drive lists its files, and must also take uploads
/// </summary>
[TestFixture]
[Category("Backend")]
public class FilejumpRootFolderTests
{
    /// <summary>
    /// Answers an empty drive and takes uploads, recording the form fields of each upload
    /// </summary>
    private sealed class EmptyDrive : HttpMessageHandler
    {
        public List<string> Uploads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/uploads", StringComparison.Ordinal))
            {
                Uploads.Add(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                return Json("{\"status\":\"success\",\"fileEntry\":{\"id\":5,\"name\":\"a.txt\",\"type\":\"file\",\"url\":\"a\"}}");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/drive/file-entries", StringComparison.Ordinal))
                return Json("{\"data\":[],\"per_page\":50}");

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Test]
    [TestCase("filejump://")]
    [TestCase("filejump:///")]
    public async Task AnUploadToTheRootIsSentWithoutAParentFolder(string url)
    {
        var drive = new EmptyDrive();
        using var backend = new Filejump(url, new Dictionary<string, string?> { ["api-token"] = "test-token" }, drive);

        using var data = new MemoryStream(Encoding.UTF8.GetBytes("content"));
        await backend.PutAsync("a.txt", data, CancellationToken.None);

        Assert.That(drive.Uploads, Has.Count.EqualTo(1));
        Assert.That(drive.Uploads[0], Does.Contain("content").And.Not.Contain("parentId"));
    }
}
