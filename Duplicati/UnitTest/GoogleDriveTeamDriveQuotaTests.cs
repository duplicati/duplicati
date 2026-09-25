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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.GoogleDrive;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A shared drive has no storage quota of its own: its files are owned by the
/// drive, and the Drive API exposes no per-drive capacity anywhere. The quota
/// the backend used to report came from the "about" resource, which describes
/// the signed-in user's own My Drive, so a backup living on a shared drive was
/// measured against a completely different pool of storage. Reported as issue
/// #4230, where a 148 GB backup was compared to a 15 GB personal quota.
/// </summary>
[TestFixture]
public class GoogleDriveTeamDriveQuotaTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    /// <summary>
    /// The option that points the backend at a shared drive. Spelling it wrong
    /// leaves the backend on the personal drive, which would quietly turn the
    /// shared drive tests into copies of the personal drive one.
    /// </summary>
    private const string TeamDriveOption = "googledrive-teamdrive-id";

    /// <summary>The quota the stubbed personal drive reports</summary>
    private const long PersonalTotal = 15_000_000_000;

    /// <summary>How much of that personal drive is in use</summary>
    private const long PersonalUsed = 1_200_000_000;

    /// <summary>An "about" answer of the shape the backend parses</summary>
    private const string PersonalAbout =
        "{\"rootFolderId\":\"root\",\"quotaBytesTotal\":15000000000,\"quotaBytesUsed\":1200000000}";

    /// <summary>
    /// Answers the two endpoints a quota lookup can reach, and records every
    /// request so a test can assert on the traffic rather than on the answer
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string m_aboutBody;
        private readonly HttpStatusCode m_aboutStatus;

        /// <summary>Every url that was requested, in order</summary>
        public List<string> Requests { get; } = new();

        public StubHandler(string aboutBody, HttpStatusCode aboutStatus)
        {
            m_aboutBody = aboutBody;
            m_aboutStatus = aboutStatus;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Requests.Add(url);

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Task.FromResult(Json("{\"access_token\":\"test-token\",\"expires\":3600}"));

            if (url.Contains("/drive/v2/about", StringComparison.Ordinal))
                return Task.FromResult(m_aboutStatus == HttpStatusCode.OK
                    ? Json(m_aboutBody)
                    : new HttpResponseMessage(m_aboutStatus));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        /// <summary>How many times the personal drive was asked about itself</summary>
        public int AboutRequests
            => Requests.Count(x => x.Contains("/drive/v2/about", StringComparison.Ordinal));

        private static HttpResponseMessage Json(string body)
            => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (GoogleDrive Backend, StubHandler Handler) Create(string? teamDriveId, string aboutBody, HttpStatusCode aboutStatus = HttpStatusCode.OK)
    {
        var handler = new StubHandler(aboutBody, aboutStatus);
        var options = new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        };

        if (teamDriveId != null)
            options[TeamDriveOption] = teamDriveId;

        return (new GoogleDrive("googledrive://target", options, new HttpClient(handler)), handler);
    }

    /// <summary>
    /// Green before and after. Without a shared drive the destination really is
    /// the user's own Drive, so the "about" figures describe it and are reported
    /// as they always were. The request assertion is what keeps
    /// <see cref="ATeamDriveAsksTheServiceNothing"/> from passing for the wrong
    /// reason: it shows the recorder sees the call it is later asked to miss.
    /// </summary>
    /// <param name="teamDriveId">
    /// The option left out, empty and blank. The backend treats all three as
    /// "personal drive" when it resolves folders (GoogleDrive.cs:93), and the
    /// option's own help says leaving it empty uses the personal drive.
    /// </param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [Category("Backend")]
    public async Task ThePersonalDriveQuotaIsReported(string? teamDriveId)
    {
        var (backend, handler) = Create(teamDriveId, PersonalAbout);
        using var _b = backend;

        var quota = await backend.GetQuotaInfoAsync(CancellationToken.None);

        Assert.IsNotNull(quota);
        Assert.AreEqual(PersonalTotal, quota!.TotalQuotaSpace);
        Assert.AreEqual(PersonalTotal - PersonalUsed, quota.FreeQuotaSpace);
        Assert.AreEqual(1, handler.AboutRequests, "the personal drive was not asked about itself");
    }

    /// <summary>
    /// The point of the issue. There is no quota to report for a shared drive,
    /// and reporting the user's own is what produced "Using 148.83 GB of 15.00
    /// GB". Saying nothing leaves the check in FilelistProcessor with nothing to
    /// compare, which is the correct answer rather than a suppressed one.
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ATeamDriveReportsNoQuota()
    {
        var (backend, _) = Create("team-drive-1", PersonalAbout);
        using var _b = backend;

        var quota = await backend.GetQuotaInfoAsync(CancellationToken.None);

        Assert.IsNull(quota, "the personal drive quota was reported for a shared drive");
    }

    /// <summary>
    /// Not the same test as <see cref="ATeamDriveReportsNoQuota"/>: fetching the
    /// personal quota and then dropping it would pass that one. The lookup is
    /// answered before any request is made, and since the OAuth token is only
    /// fetched when a request is built, that means no traffic at all.
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ATeamDriveAsksTheServiceNothing()
    {
        var (backend, handler) = Create("team-drive-1", PersonalAbout);
        using var _b = backend;

        await backend.GetQuotaInfoAsync(CancellationToken.None);

        Assert.AreEqual(0, handler.Requests.Count, $"requested: {string.Join(", ", handler.Requests)}");
    }

    /// <summary>
    /// Green before and after, and it guards the -1 that the caller reads:
    /// FilelistProcessor.cs:589 skips the warning on a negative free space with
    /// the comment "Negative value means the backend didn't return the quota
    /// info". Note the precedence in "total - used ?? -1", which binds as
    /// "(total - used) ?? -1", so either field being absent yields -1.
    /// </summary>
    [TestCase("{\"rootFolderId\":\"root\"}", -1L, -1L)]
    [TestCase("{\"rootFolderId\":\"root\",\"quotaBytesTotal\":15000000000}", PersonalTotal, -1L)]
    [Category("Backend")]
    public async Task AnAboutAnswerWithoutQuotaFiguresReportsUnknown(string aboutBody, long expectedTotal, long expectedFree)
    {
        var (backend, _) = Create(null, aboutBody);
        using var _b = backend;

        var quota = await backend.GetQuotaInfoAsync(CancellationToken.None);

        Assert.IsNotNull(quota);
        Assert.AreEqual(expectedTotal, quota!.TotalQuotaSpace);
        Assert.AreEqual(expectedFree, quota.FreeQuotaSpace);
    }

    /// <summary>
    /// Green before and after. The shared drive answer is returned before the
    /// request is attempted, so this is the test that notices if that early
    /// return is ever folded into the catch that handles a failed lookup.
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task AFailedLookupReportsNoQuota()
    {
        var (backend, _) = Create(null, PersonalAbout, HttpStatusCode.InternalServerError);
        using var _b = backend;

        var quota = await backend.GetQuotaInfoAsync(CancellationToken.None);

        Assert.IsNull(quota);
    }
}
