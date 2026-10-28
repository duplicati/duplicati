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

#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Duplicati.Library.Utility.Options;
using Duplicati.Proprietary.Office365;

namespace Duplicati.UnitTest;

/// <summary>
/// Builds the Office 365 API helpers the tests use to answer Graph requests from a fake
/// </summary>
internal static class Office365TestSupport
{
    /// <summary>
    /// The access token the fake Graph is sent
    /// </summary>
    public const string ACCESS_TOKEN = "test-token";

    /// <summary>
    /// Creates an API helper that sends its requests through a handler and uses a fixed access
    /// token, so it never contacts the token endpoint
    /// </summary>
    /// <param name="handler">The handler that answers the requests</param>
    /// <param name="graphBaseUrl">The Graph base URL</param>
    /// <param name="timeouts">The timeouts to use</param>
    /// <returns>The API helper</returns>
    public static APIHelper CreateApiHelper(HttpMessageHandler handler, string graphBaseUrl, TimeoutOptionsHelper.Timeouts timeouts)
    {
        // The handler is shared between the helpers of every provider instance a test creates, so it is not disposed with the client
        var httpClient = new HttpClient(handler, disposeHandler: false);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        var authOptions = AuthOptionsHelper.ParseWithAlias(new Dictionary<string, string?>(), new Duplicati.Library.Utility.Uri("https://test:test@localhost/"), "test-user", "test-pass");
        return new APIHelper(httpClient, authOptions, "test-tenant", graphBaseUrl, timeouts, null, null, "https://graph.microsoft.com/.default", ACCESS_TOKEN);
    }

    /// <summary>
    /// Makes the providers created in the current asynchronous flow, for instance by a backup
    /// through the loader, send their Graph requests through a handler until the scope is disposed
    /// </summary>
    /// <param name="handler">The handler that answers the requests</param>
    /// <returns>The scope</returns>
    public static IDisposable UseGraphHandler(HttpMessageHandler handler)
        => SourceProvider.UseApiHelperFactory((parsed, timeouts) => CreateApiHelper(handler, parsed.GraphBaseUrl, timeouts));
}
