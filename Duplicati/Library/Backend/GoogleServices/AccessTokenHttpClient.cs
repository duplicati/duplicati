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

using System.Net.Http.Headers;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend.GoogleServices
{
    /// <summary>
    /// Minimal HTTP client that adds an access token obtained elsewhere to outgoing requests.
    /// The token is used as it is and is not renewed.
    /// </summary>
    internal class AccessTokenHttpClient : JsonWebHelperHttpClient
    {
        /// <summary>
        /// The access token sent with every request
        /// </summary>
        private readonly string _accessToken;

        private static HttpClient CreateHttpClientWithInfiniteTimeout()
        {
            var client = HttpClientHelper.CreateClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            return client;
        }

        /// <summary>
        /// Creates a client that authenticates with the supplied access token
        /// </summary>
        /// <param name="accessToken">The access token</param>
        /// <param name="httpClient">The client to send the requests through, or <c>null</c> to use the default; used for testing</param>
        public AccessTokenHttpClient(string accessToken, HttpClient? httpClient)
            : base(httpClient ?? CreateHttpClientWithInfiniteTimeout())
        {
            _accessToken = accessToken;
        }

        /// <inheritdoc />
        public override async Task<HttpRequestMessage> CreateRequestAsync(string url, HttpMethod method, CancellationToken cancellationToken)
        {
            var req = await base.CreateRequestAsync(url, method, cancellationToken).ConfigureAwait(false);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return req;
        }

        /// <inheritdoc />
        public override Task AttemptParseAndThrowExceptionAsync(Exception ex, HttpResponseMessage? responseContext, CancellationToken cancellationToken)
            => GoogleCommon.ThrowWithErrorReasonAsync(ex, responseContext, cancellationToken);
    }
}
