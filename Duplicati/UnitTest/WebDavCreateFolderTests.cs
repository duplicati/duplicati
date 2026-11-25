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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A WebDAV server creates a single folder at a time, and answers with a conflict when the parent
/// does not exist, so a destination with two new folders could not be created.
/// </summary>
[TestFixture]
[Category("Backend")]
public class WebDavCreateFolderTests
{
    /// <summary>
    /// A server with folders, that answers MKCOL as RFC 4918 says
    /// </summary>
    private sealed class FolderHandler(params string[] folders) : HttpMessageHandler
    {
        public HashSet<string> Folders { get; } = [.. folders];
        public List<string> Requests { get; } = [];
        public HttpStatusCode? Refuse { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");

            var name = path.TrimEnd('/');
            var parent = name[..(name.LastIndexOf('/') + 1)];
            var status = Folders.Contains(path) ? HttpStatusCode.MethodNotAllowed
                : !Folders.Contains(parent) ? HttpStatusCode.Conflict
                : Refuse ?? HttpStatusCode.Created;
            if (status == HttpStatusCode.Created)
                Folders.Add(path);

            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static Task CreateAsync(string url, FolderHandler handler)
        => new WEBDAV(url, new Dictionary<string, string?>(), handler).CreateFolderAsync(CancellationToken.None);

    [Test]
    [TestCase("webdav://host/dav/backup/", "/dav/backup/")]
    [TestCase("webdav://host/dav/backup/job", "/dav/backup/job/")]
    [TestCase("webdav://host/dav/a/b/c/d/", "/dav/a/b/c/d/")]
    [TestCase("webdav://host:8080/dav/my%20backup/a%2Bb/", "/dav/my%20backup/a%2Bb/")]
    public async Task AFolderIsCreatedWithItsParents(string url, string expected)
    {
        var handler = new FolderHandler("/", "/dav/");
        await CreateAsync(url, handler);
        Assert.That(handler.Folders, Does.Contain(expected), string.Join(Environment.NewLine, handler.Requests));
    }

    [Test]
    public async Task AFolderWithAParentNeedsOneRequest()
    {
        var handler = new FolderHandler("/", "/dav/");
        await CreateAsync("webdav://host/dav/backup/", handler);
        Assert.That(handler.Requests, Is.EqualTo(new[] { "MKCOL /dav/backup/" }));
    }

    [Test]
    public void ARefusedParentFailsWithTheErrorOfTheFolder()
    {
        var handler = new FolderHandler("/", "/dav/") { Refuse = HttpStatusCode.Forbidden };
        var ex = Assert.ThrowsAsync<HttpRequestException>(() => CreateAsync("webdav://host/dav/backup/job/", handler));
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(handler.Requests, Is.EqualTo(new[] { "MKCOL /dav/backup/job/", "MKCOL /dav/backup/" }));
    }

    [Test]
    public void AServerThatAlwaysGivesAConflictIsNotAskedForever()
    {
        var handler = new FolderHandler();
        var ex = Assert.ThrowsAsync<HttpRequestException>(() => CreateAsync("webdav://host/a/b/c/", handler));
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(handler.Requests, Has.Count.EqualTo(3));
    }

    [Test]
    public void AFolderThatExistsStillFails()
    {
        var handler = new FolderHandler("/", "/dav/", "/dav/backup/");
        Assert.ThrowsAsync<HttpRequestException>(() => CreateAsync("webdav://host/dav/backup/", handler));
        Assert.That(handler.Requests.Single(), Is.EqualTo("MKCOL /dav/backup/"));
    }
}
