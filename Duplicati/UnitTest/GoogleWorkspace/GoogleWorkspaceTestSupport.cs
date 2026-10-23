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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Duplicati.Proprietary.GoogleWorkspace;
using Google.Apis.Http;

namespace Duplicati.UnitTest.GoogleWorkspace;

/// <summary>
/// Builds the Google Workspace API helpers the tests use to answer the Google API requests
/// from a fake, and the response builders the fakes share
/// </summary>
internal static class GoogleWorkspaceTestSupport
{
    /// <summary>
    /// The access token the fake services are sent
    /// </summary>
    public const string ACCESS_TOKEN = "test-token";

    /// <summary>
    /// The boundary of the multipart batch responses
    /// </summary>
    private const string BOUNDARY = "batch_test_boundary";

    /// <summary>
    /// Hands every service client the same handler
    /// </summary>
    public sealed class ScriptedClientFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }

    /// <summary>
    /// Creates an API helper whose services send their requests through a handler and use a
    /// fixed access token, so they never contact the token endpoint
    /// </summary>
    /// <param name="handler">The handler that answers the requests</param>
    /// <param name="options">The parsed provider options</param>
    /// <param name="isRestoreOperation">Whether the helper is used for a restore</param>
    /// <returns>The API helper</returns>
    public static APIHelper CreateApiHelper(HttpMessageHandler handler, OptionsHelper.GoogleWorkspaceOptions options, bool isRestoreOperation)
        => new(options, isRestoreOperation, new ScriptedClientFactory(handler), ACCESS_TOKEN);

    /// <summary>
    /// Makes the providers created in the current asynchronous flow, for instance by a backup
    /// through the loader, send their requests through a handler until the scope is disposed
    /// </summary>
    /// <param name="handler">The handler that answers the requests</param>
    /// <returns>The scope</returns>
    public static IDisposable UseHandler(HttpMessageHandler handler)
        => SourceProvider.UseApiHelperFactory((options, restore) => CreateApiHelper(handler, options, restore));

    /// <summary>
    /// A JSON response
    /// </summary>
    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// A Google error response in the legacy format, which carries a reason per error
    /// </summary>
    public static HttpResponseMessage GoogleError(HttpStatusCode status, string reason, string message)
        => Json(status, $"{{\"error\":{{\"code\":{(int)status},\"message\":\"{message}\",\"errors\":[{{\"message\":\"{message}\",\"domain\":\"global\",\"reason\":\"{reason}\"}}]}}}}");

    /// <summary>
    /// The message ids a Gmail batch request asks for, in order
    /// </summary>
    public static List<string> BatchRequestedIds(string? batchBody)
        => Regex.Matches(batchBody ?? "", @"/messages/([^?\s]+)").Select(m => Uri.UnescapeDataString(m.Groups[1].Value)).ToList();

    /// <summary>
    /// Builds a batch response in the shape Google sends: one application/http part per
    /// request, each wrapping a complete HTTP response, in request order
    /// </summary>
    public static HttpResponseMessage BatchResponse(IEnumerable<(HttpStatusCode Status, string Json)> parts)
    {
        var sb = new StringBuilder();
        var i = 0;
        foreach (var (status, json) in parts)
        {
            i++;
            sb.Append("--").Append(BOUNDARY).Append("\r\n");
            sb.Append("Content-Type: application/http\r\n");
            sb.Append($"Content-ID: <response-{i}>\r\n\r\n");
            sb.Append($"HTTP/1.1 {(int)status} {status}\r\n");
            sb.Append("Content-Type: application/json; charset=UTF-8\r\n");
            sb.Append($"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n");
            sb.Append(json).Append("\r\n");
        }
        sb.Append("--").Append(BOUNDARY).Append("--\r\n");

        var content = new StringContent(sb.ToString());
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/mixed");
        content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", BOUNDARY));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>
    /// The not-found body of a batch part
    /// </summary>
    public const string NOT_FOUND_PART = """{"error":{"code":404,"message":"Requested entity was not found.","errors":[{"message":"Requested entity was not found.","domain":"global","reason":"notFound"}]}}""";

    /// <summary>
    /// The base64url form of a byte array, as Gmail returns raw messages
    /// </summary>
    public static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
