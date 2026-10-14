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
/// A server lists a folder by answering PROPFIND with the path of each entry, often without the
/// scheme and host. The backend has to turn that path back into the file name. Reported as issue
/// #6748, where a "#" in the folder name left the folder in front of every file name on Linux.
/// </summary>
[TestFixture]
public class WebDavListNameTests
{
    private const string FileName = "duplicati-20260101T000000Z.dlist.zip.aes";

    /// <summary>
    /// Answers PROPFIND with the folder and one file in it, both given by the supplied href
    /// </summary>
    private sealed class PropfindHandler(string folderHref) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <D:multistatus xmlns:D="DAV:">
                  <D:response>
                    <D:href>{folderHref}</D:href>
                    <D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
                  </D:response>
                  <D:response>
                    <D:href>{folderHref}{FileName}</D:href>
                    <D:propstat><D:prop><D:getcontentlength>10</D:getcontentlength><D:resourcetype/></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
                  </D:response>
                </D:multistatus>
                """;

            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)207)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml")
            });
        }
    }

    /// <summary>
    /// Lists the folder and returns the names of the files in it
    /// </summary>
    private static async Task<List<string>> ListedFileNamesAsync(string url, string folderHref)
    {
        using var backend = new WEBDAV(url, new Dictionary<string, string?>(), new PropfindHandler(folderHref));
        var names = new List<string>();
        await foreach (var entry in backend.ListAsync(CancellationToken.None))
            if (!entry.IsFolder)
                names.Add(entry.Name);
        return names;
    }

    /// <summary>
    /// The folder of issue #6748, and a folder with a space, given by their path
    /// </summary>
    [TestCase("webdav://host/dav/Duplicati/%23Photo/", "/dav/Duplicati/%23Photo/")]
    [TestCase("webdav://host/remote.php/dav/files/user/My%20Folder/", "/remote.php/dav/files/user/My%20Folder/")]
    [TestCase("webdav://host/Backup/", "/Backup/")]
    // The same folders given by the full url
    [TestCase("webdav://host/dav/Duplicati/%23Photo/", "http://host/dav/Duplicati/%23Photo/")]
    [TestCase("webdav://host/remote.php/dav/files/user/My%20Folder/", "http://host/remote.php/dav/files/user/My%20Folder/")]
    public async Task TheListedNameIsTheFileNameAsync(string url, string folderHref)
        => Assert.That(await ListedFileNamesAsync(url, folderHref), Is.EqualTo(new[] { FileName }));
}
