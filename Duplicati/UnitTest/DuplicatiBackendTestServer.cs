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
using Duplicati.Library.Backend.Duplicati;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A stand-in for the Duplicati storage server, shared by the tests of the Duplicati backend
/// </summary>
internal static class DuplicatiBackendTestServer
{
    /// <summary>
    /// A request the fake server has received
    /// </summary>
    /// <param name="Method">The HTTP method</param>
    /// <param name="Path">The request path</param>
    /// <param name="Query">The query string, including the leading question mark, or empty</param>
    /// <param name="Body">The request body, or <c>null</c> if the request had none</param>
    public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body);

    /// <summary>
    /// A message handler that answers requests from a table of paths
    /// </summary>
    public sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _responses = new();

        /// <summary>
        /// The paths of the requests received, in order
        /// </summary>
        public List<string> Requests { get; } = [];

        /// <summary>
        /// The requests received, in order, with their method and body
        /// </summary>
        public List<RecordedRequest> RecordedRequests { get; } = [];

        /// <summary>
        /// Queues a plain text response for a path; the last queued response is repeated
        /// </summary>
        public FakeServer On(string path, HttpStatusCode status, string body)
            => On(path, status, body, "text/plain");

        /// <summary>
        /// Queues a response for a path; the last queued response is repeated
        /// </summary>
        public FakeServer On(string path, HttpStatusCode status, string body, string mediaType)
        {
            if (!_responses.TryGetValue(path, out var queue))
                _responses[path] = queue = new Queue<Func<HttpResponseMessage>>();
            queue.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            var body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            RecordedRequests.Add(new RecordedRequest(request.Method, path, request.RequestUri.Query, body));

            foreach (var (prefix, queue) in _responses)
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                    return (queue.Count > 1 ? queue.Dequeue() : queue.Peek())();

            return new HttpResponseMessage(HttpStatusCode.NotImplemented) { Content = new StringContent(string.Empty) };
        }
    }

    public const string GatewayCredentials = "{\"mode\":\"gateway\",\"uploadsDisabled\":false,\"accountDisabled\":false}";
    public const string QuotaExceededCredentials = "{\"mode\":\"gateway\",\"uploadsDisabled\":true,\"uploadsDisabledReason\":\"quota-exceeded\",\"accountDisabled\":false}";
    public const string DisabledGatewayCredentials = "{\"mode\":\"gateway\",\"uploadsDisabled\":true,\"uploadsDisabledReason\":\"account-disabled\",\"accountDisabled\":true,\"accountDisabledReason\":\"no-quota\",\"accountDisabledMessage\":\"Account subscription does not have access to storage\"}";
    public const string DisabledDirectCredentials = "{\"mode\":\"direct\",\"uploadsDisabled\":true,\"uploadsDisabledReason\":\"account-disabled\",\"accountDisabled\":true,\"accountDisabledReason\":\"provider-account-inactive\",\"accountDisabledMessage\":\"The iDrive storage account for this organization is disabled\"}";

    /// <summary>
    /// Creates a backend in gateway mode that talks to the fake server
    /// </summary>
    /// <param name="server">The fake server</param>
    /// <returns>The backend</returns>
    public static DuplicatiBackend CreateBackend(FakeServer server)
        => CreateBackend(server, new Dictionary<string, string?>());

    /// <summary>
    /// Creates a backend that talks to the fake server, with extra options on top of the credentials
    /// </summary>
    /// <param name="server">The fake server</param>
    /// <param name="extraOptions">The options to add</param>
    /// <returns>The backend</returns>
    public static DuplicatiBackend CreateBackend(FakeServer server, Dictionary<string, string?> extraOptions)
    {
        var options = new Dictionary<string, string?>
        {
            [DuplicatiBackend.BACKUP_ID_OPTION] = "backup-1",
            [DuplicatiBackend.AUTH_API_ID_OPTION] = "org-1",
            [DuplicatiBackend.AUTH_API_KEY_OPTION] = "key-1",
            ["machine-id"] = "machine-1",
        };
        foreach (var (key, value) in extraOptions)
            options[key] = value;

        return new DuplicatiBackend("duplicati://storage.example.com", options, server);
    }
}
