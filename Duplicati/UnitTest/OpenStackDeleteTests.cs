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
using Duplicati.Library.Backend.OpenStack;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The Object Storage API answers the delete of an object that is not there with
/// 404, and this backend already knows that: GetAsync reports such a 404 as a
/// FileMissingException. DeleteAsync did not, so the destination self-test could
/// not tell "the probe file is already gone" from a destination that does not work.
/// </summary>
[TestFixture]
public class OpenStackDeleteTests
{
    /// <summary>The container the backend is pointed at</summary>
    private const string Container = "duplicati-container";

    /// <summary>The name the tests delete</summary>
    private const string RemoteName = "duplicati-b0123456789.dblock.zip.aes";

    /// <summary>Where the catalog says the object store lives</summary>
    private const string StorageEndpoint = "https://swift.invalid/v1/AUTH_test";

    /// <summary>The v2 authentication endpoint the tests point the backend at</summary>
    private const string AuthUri = "https://keystone.invalid/v2.0";

    /// <summary>
    /// Answers the keystone request with a catalog, and lets the test decide what
    /// the object store says. Records every request it is given.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>
        /// Runs for each request that is not the authentication call
        /// </summary>
        public Func<HttpRequestMessage, HttpResponseMessage> OnStorageRequest { get; set; }
            = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        /// <summary>
        /// The requests the object store was given, in order
        /// </summary>
        public List<(HttpMethod Method, string Url)> StorageRequests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.StartsWith(AuthUri, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"access\":{\"token\":{\"id\":\"test-token\"}," +
                        "\"serviceCatalog\":[{\"type\":\"object-store\",\"endpoints\":[{\"publicURL\":\"" + StorageEndpoint + "\"}]}]}}",
                        Encoding.UTF8, "application/json")
                });

            StorageRequests.Add((request.Method, url));
            return Task.FromResult(OnStorageRequest(request));
        }
    }

    private static (OpenStackStorage Backend, StubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage>? onStorageRequest = null)
    {
        var handler = new StubHandler();
        if (onStorageRequest != null)
            handler.OnStorageRequest = onStorageRequest;

        var options = new Dictionary<string, string?>
        {
            ["auth-username"] = "user",
            ["auth-password"] = "pass",
            ["openstack-tenant-name"] = "tenant",
            ["openstack-authuri"] = AuthUri
        };

        return (new OpenStackStorage($"openstack://{Container}", options, handler), handler);
    }

    private static HttpResponseMessage Status(HttpStatusCode code)
        => new(code) { Content = new StringContent("", Encoding.UTF8, "text/plain") };

    /// <summary>
    /// The point of the change: an object that is not there has to arrive as a
    /// FileMissingException, which is what the callers look for
    /// </summary>
    [Test]
    [Category("Backend")]
    public void ADeleteOfAFileThatIsGoneIsReportedAsMissing()
    {
        var (backend, _) = Create(_ => Status(HttpStatusCode.NotFound));
        using var _b = backend;

        Assert.CatchAsync<FileMissingException>(async () =>
            await backend.DeleteAsync(RemoteName, CancellationToken.None));
    }

    /// <summary>
    /// Green before and after: only the 404 changed meaning, everything else still
    /// comes out as the transport error it is
    /// </summary>
    [Test]
    [Category("Backend")]
    public void ADeleteThatFailsForAnotherReasonIsNotReportedAsMissing()
    {
        var (backend, _) = Create(_ => Status(HttpStatusCode.InternalServerError));
        using var _b = backend;

        var ex = Assert.CatchAsync(async () =>
            await backend.DeleteAsync(RemoteName, CancellationToken.None));

        Assert.IsNotInstanceOf<FileMissingException>(ex);
    }

    /// <summary>
    /// Green before and after: a delete the store accepted stays quiet
    /// </summary>
    [Test]
    [Category("Backend")]
    public void ASuccessfulDeleteDoesNotThrow()
    {
        var (backend, _) = Create(_ => Status(HttpStatusCode.NoContent));
        using var _b = backend;

        Assert.DoesNotThrowAsync(async () =>
            await backend.DeleteAsync(RemoteName, CancellationToken.None));
    }

    /// <summary>
    /// Green before and after, and the reason the rest of this measures anything:
    /// the request that goes out is the delete of that object in that container
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task TheDeleteGoesToTheObjectStoreEndpoint()
    {
        var (backend, handler) = Create(_ => Status(HttpStatusCode.NoContent));
        using var _b = backend;

        await backend.DeleteAsync(RemoteName, CancellationToken.None);

        Assert.AreEqual(1, handler.StorageRequests.Count);
        Assert.AreEqual(HttpMethod.Delete, handler.StorageRequests[0].Method);
        Assert.AreEqual($"{StorageEndpoint}/{Container}/{RemoteName}", handler.StorageRequests[0].Url);
    }
}
