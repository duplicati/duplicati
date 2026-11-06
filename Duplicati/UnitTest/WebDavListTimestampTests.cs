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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A server reports the modification time of a file in the PROPFIND answer, normally as an RFC 1123
/// date in GMT. The backend has to report it as UTC: a plain parse converts it to local time, and the
/// remote synchronization planner then compares it against the UTC timestamps of other backends,
/// which west of UTC makes every destination file look older than its source.
/// </summary>
[TestFixture]
public class WebDavListTimestampTests
{
    private const string FileName = "duplicati-20260101T000000Z.dlist.zip.aes";

    /// <summary>
    /// Answers PROPFIND with one file carrying the supplied getlastmodified value
    /// </summary>
    private sealed class PropfindHandler(string lastModified) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <D:multistatus xmlns:D="DAV:">
                  <D:response>
                    <D:href>/Backup/</D:href>
                    <D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
                  </D:response>
                  <D:response>
                    <D:href>/Backup/{FileName}</D:href>
                    <D:propstat><D:prop><D:getcontentlength>10</D:getcontentlength><D:getlastmodified>{lastModified}</D:getlastmodified><D:resourcetype/></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
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
    /// Lists the folder and returns the one file in it
    /// </summary>
    private static async Task<IFileEntry> ListedFileAsync(string lastModified)
    {
        using var backend = new WEBDAV("webdav://host/Backup/", new Dictionary<string, string?>(), new PropfindHandler(lastModified));
        await foreach (var entry in backend.ListAsync(CancellationToken.None))
            if (!entry.IsFolder)
                return entry;
        throw new InvalidOperationException("No file listed");
    }

    /// <summary>
    /// The RFC 1123 form every server uses, the same instant with an explicit offset, and a value without a zone
    /// </summary>
    [TestCase("Tue, 03 Nov 2026 20:30:44 GMT")]
    [TestCase("2026-11-03T20:30:44Z")]
    [TestCase("2026-11-03T15:30:44-05:00")]
    [TestCase("2026-11-03T20:30:44")]
    public async Task TheModificationTimeIsReportedInUtcAsync(string lastModified)
    {
        var entry = await ListedFileAsync(lastModified);
        Assert.That(entry.LastModification.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(entry.LastModification, Is.EqualTo(new DateTime(2026, 11, 3, 20, 30, 44, DateTimeKind.Utc)));
        Assert.That(entry.LastAccess, Is.EqualTo(entry.LastModification));
    }
}
