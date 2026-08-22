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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// What a destination url means is only visible in the request the backend then sends, so this
/// asks the backend where it is going rather than asking a helper what it would answer.
/// Reported as issue #4880, where a "+" in the path sent every request to a different folder.
/// </summary>
[TestFixture]
public class WebDavRequestUrlTests
{
    /// <summary>
    /// Answers every request with "created" and keeps the url it was asked for.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        }
    }

    private static async Task<string> WhereDoesItGoAsync(string url)
    {
        var handler = new RecordingHandler();
        using var backend = new WEBDAV(url, new Dictionary<string, string?>(), handler);

        await backend.CreateFolderAsync(CancellationToken.None);

        Assert.AreEqual("MKCOL", handler.LastMethod?.Method, "The backend did not send the request");
        return handler.LastUri!.AbsoluteUri;
    }

    /// <summary>
    /// A "+" is a character in a path. Only a query string spells a space that way, so reading it
    /// as one sends every request to a folder the user does not have.
    /// </summary>
    [TestCase("webdav://host/My+Folder/", "http://host/My+Folder/")]
    // "%2B" is the other spelling of the same character, and the server reads it the same way,
    // so this one only has to stay a "+" rather than turn into a space.
    [TestCase("webdav://host/My%2BFolder/", "http://host/My%2BFolder/")]
    [TestCase("webdav://host/My%20Folder/", "http://host/My%20Folder/")]
    [TestCase("webdav://host/M%C3%A4ppe/", "http://host/M%C3%A4ppe/")]
    [TestCase("webdav://host/Backup/", "http://host/Backup/")]
    [TestCase("webdav://host/Backup", "http://host/Backup/")]
    [TestCase("webdav://host:8080/Backup/", "http://host:8080/Backup/")]
    [TestCase("webdav://host/", "http://host/")]
    public async Task TheRequestGoesToTheFolderTheUserNamedAsync(string url, string expected)
        => Assert.AreEqual(expected, await WhereDoesItGoAsync(url), url);

    /// <summary>
    /// A folder whose name contains a percent sign is spelled with the percent encoded, and it has
    /// to survive being decoded exactly once.
    /// </summary>
    [Test]
    public async Task ThePathIsDecodedOnceAsync()
        => Assert.AreEqual("http://host/a%2520b/", await WhereDoesItGoAsync("webdav://host/a%2520b/"));

    /// <summary>
    /// The backend loader picks the constructor by argument count, so the one the tests use must
    /// not become the one it finds.
    /// </summary>
    [Test]
    public void TheLoaderStillFindsTheTwoArgumentConstructor()
    {
        using var backend = (IBackend)Activator.CreateInstance(
            typeof(WEBDAV), "webdav://host/backup/", new Dictionary<string, string?>())!;

        Assert.AreEqual("webdav", backend.ProtocolKey);
    }
}
