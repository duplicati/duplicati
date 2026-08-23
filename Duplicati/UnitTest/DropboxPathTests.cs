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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The destination url names a folder in the Dropbox account, and the backend passes that name
/// to the API in the body of the request. What arrives there is what these tests read, because a
/// folder name only means something once it has been sent.
/// </summary>
[TestFixture]
public class DropboxPathTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails instead of reaching
    /// the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    /// <summary>
    /// Answers every call with an empty object and keeps what was asked for
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>
    /// Creates the folder the url names, and returns the path the request carried.
    /// </summary>
    private static async Task<string?> WhichFolderIsAskedForAsync(string url)
    {
        var handler = new RecordingHandler();
        using var backend = new Dropbox(url, new Dictionary<string, string?>
        {
            // No colon, so the token is used directly and no OAuth call is made
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }, new HttpClient(handler));

        await backend.CreateFolderAsync(CancellationToken.None);

        Assert.IsNotNull(handler.LastBody, "The backend sent no request");
        return JsonNode.Parse(handler.LastBody!)?["path"]?.GetValue<string>();
    }

    /// <summary>
    /// A folder whose name contains a percent sign is spelled with the percent encoded, and the
    /// name has to survive being decoded exactly once on the way to the API.
    /// </summary>
    [TestCase("dropbox://a%2520b", "/a%20b")]
    [TestCase("dropbox://Backup/a%2520b", "/Backup/a%20b")]
    [TestCase("dropbox://100%2525done", "/100%25done")]
    public async Task APercentInTheFolderNameSurvivesAsync(string url, string expected)
        => Assert.AreEqual(expected, await WhichFolderIsAskedForAsync(url), url);

    [TestCase("dropbox://Backup", "/Backup")]
    [TestCase("dropbox://Backup/", "/Backup")]
    [TestCase("dropbox://Backup/Sub", "/Backup/Sub")]
    [TestCase("dropbox://My%20Folder", "/My Folder")]
    [TestCase("dropbox://M%C3%A4ppe", "/Mäppe")]
    [TestCase("dropbox://Backup/M%C3%A4ppe/Sub", "/Backup/Mäppe/Sub")]
    public async Task TheRestOfTheDecodingIsUnchangedAsync(string url, string expected)
        => Assert.AreEqual(expected, await WhichFolderIsAskedForAsync(url), url);

    [Test]
    public async Task APlusIsStillReadAsASpaceAsync()
    {
        // Not what a path means, and the same defect issue #4880 reported for WebDAV, but this one
        // cannot be fixed here: it happens inside the shared parser, and this backend cannot move
        // to System.Uri because the first segment of its url is a folder name, which is case
        // sensitive. Pinned so the day it changes is not a surprise.
        Assert.AreEqual("/My Folder", await WhichFolderIsAskedForAsync("dropbox://My+Folder"));
        Assert.AreEqual("/My+Folder", await WhichFolderIsAskedForAsync("dropbox://My%2BFolder"));
    }

    /// <summary>
    /// The backend loader picks the constructor by argument count, so the one the tests use must
    /// not become the one it finds.
    /// </summary>
    [Test]
    public void TheLoaderStillFindsTheTwoArgumentConstructor()
    {
        using var backend = (IBackend)Activator.CreateInstance(
            typeof(Dropbox), "dropbox://Backup", new Dictionary<string, string?>
            {
                ["authid"] = "test-authid",
                ["oauth-url"] = OAuthUrl
            })!;

        Assert.AreEqual("dropbox", backend.ProtocolKey);
    }
}
