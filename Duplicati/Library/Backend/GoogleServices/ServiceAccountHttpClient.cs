// Copyright (C) 2025, The Duplicati Team
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
using Google.Apis.Auth.OAuth2;

namespace Duplicati.Library.Backend.GoogleServices
{
    /// <summary>
    /// Minimal HTTP client that adds a service account access token to outgoing requests.
    /// </summary>
    internal class ServiceAccountHttpClient : JsonWebHelperHttpClient
    {
        private readonly ITokenAccess _credential;

        private static HttpClient CreateHttpClientWithInfiniteTimeout()
        {
            var client = HttpClientHelper.CreateClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            return client;
        }

        public ServiceAccountHttpClient(GoogleCredential credential)
            : base(CreateHttpClientWithInfiniteTimeout())
        {
            _credential = (ITokenAccess)credential;
        }

        public override async Task<HttpRequestMessage> CreateRequestAsync(string url, HttpMethod method, CancellationToken cancellationToken)
        {
            var req = await base.CreateRequestAsync(url, method, cancellationToken).ConfigureAwait(false);
            var token = await _credential.GetAccessTokenForRequestAsync(null, cancellationToken).ConfigureAwait(false);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return req;
        }
    }
}

