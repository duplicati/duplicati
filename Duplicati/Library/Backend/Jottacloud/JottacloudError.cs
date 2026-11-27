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

using System.Net;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Duplicati.Library.Backend;

/// <summary>
/// The error a Jottacloud service returns with a failed request. The JFS hosts answer
/// with an XML document, the newer API and the upload hosts with a JSON document.
/// </summary>
/// <param name="ErrorId">The name of the server-side error, such as "CorruptUploadOpenApiException"</param>
/// <param name="Message">The message from the server</param>
/// <param name="Cause">The cause from the server</param>
/// <param name="XId">The request id, which Jottacloud support can look up</param>
internal sealed record JottacloudError(string? ErrorId, string? Message, string? Cause, string? XId)
{
    /// <summary>
    /// The error the upload host answers with when the content does not match the size and MD5 hash of the allocation
    /// </summary>
    private const string CORRUPT_UPLOAD = "CorruptUploadOpenApiException";

    /// <summary>
    /// The error the upload host answers with when it has no matching allocation
    /// </summary>
    private const string INCOMPLETE_UPLOAD = "IncompleteUploadOpenApiException";

    /// <summary>
    /// The path the upload URLs from an allocation start with
    /// </summary>
    private const string UPLOAD_PATH = "/files/v1/upload/";

    /// <summary>
    /// Reads the error from the body of a failed response
    /// </summary>
    /// <param name="response">The failed response</param>
    /// <param name="maxLength">The longest body to read</param>
    /// <param name="timeout">How long to wait for the body</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The error, or <c>null</c> if the body is not a Jottacloud error</returns>
    public static async Task<JottacloudError?> TryReadAsync(HttpResponseMessage response, int maxLength, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            var buffer = new byte[maxLength];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), cts.Token).ConfigureAwait(false)) > 0)
                length += read;

            body = Encoding.UTF8.GetString(buffer, 0, length).Trim();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // The error is still reported, just without the explanation from the body
            return null;
        }

        if (body.StartsWith('{'))
            return ParseJson(body);
        if (body.StartsWith('<'))
            return ParseXml(body);
        return null;
    }

    /// <summary>
    /// Parses an error from the newer API or the upload hosts, such as
    /// <c>{"code":421,"message":null,"cause":"","error_id":"CorruptUploadOpenApiException","x-id":"067180364423"}</c>
    /// </summary>
    /// <param name="body">The response body</param>
    /// <returns>The error, or <c>null</c> if the body cannot be parsed</returns>
    private static JottacloudError? ParseJson(string body)
    {
        try
        {
            var json = JObject.Parse(body);
            return Create((string?)json["error_id"], (string?)json["message"], (string?)json["cause"], (string?)json["x-id"]);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidCastException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses an error from the JFS hosts, which is an <c>error</c> element with
    /// <c>code</c>, <c>message</c>, <c>reason</c>, <c>cause</c> and <c>x-id</c> elements
    /// </summary>
    /// <param name="body">The response body</param>
    /// <returns>The error, or <c>null</c> if the body cannot be parsed</returns>
    private static JottacloudError? ParseXml(string body)
    {
        try
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(body), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc.Load(reader);

            var root = doc.DocumentElement;
            if (root == null || root.Name != "error")
                return null;

            return Create(null, root["message"]?.InnerText, root["cause"]?.InnerText, root["x-id"]?.InnerText);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates an error with empty values left out
    /// </summary>
    private static JottacloudError? Create(string? errorId, string? message, string? cause, string? xid)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var error = new JottacloudError(Clean(errorId), Clean(message), Clean(cause), Clean(xid));
        return error.ErrorId == null && error.Message == null && error.Cause == null && error.XId == null
            ? null
            : error;
    }

    /// <summary>
    /// Explains a failed request
    /// </summary>
    /// <param name="error">The error from the response body, if any</param>
    /// <param name="statusCode">The status code of the response</param>
    /// <param name="requestUri">The URL of the request</param>
    /// <returns>The explanation, or <c>null</c> if there is nothing to add to the status code</returns>
    public static string? Describe(JottacloudError? error, HttpStatusCode statusCode, Uri? requestUri)
    {
        var isUpload = requestUri?.AbsolutePath.StartsWith(UPLOAD_PATH, StringComparison.Ordinal) ?? false;

        var explanation = error?.ErrorId switch
        {
            CORRUPT_UPLOAD => Strings.Jottacloud.CorruptUploadError,
            INCOMPLETE_UPLOAD => Strings.Jottacloud.IncompleteUploadError,
            _ => statusCode switch
            {
                HttpStatusCode.Conflict when isUpload => Strings.Jottacloud.UploadAllocationGoneError,
                HttpStatusCode.TooManyRequests => Strings.Jottacloud.TooManyRequestsError,
                _ => error?.Cause ?? error?.Message
            }
        };

        var details = new List<string>();
        if (error?.ErrorId != null)
            details.Add(error.ErrorId);
        if (error?.XId != null)
            details.Add("x-id: " + error.XId);

        if (explanation == null)
            return details.Count == 0 ? null : string.Join(", ", details);

        return details.Count == 0 ? explanation : $"{explanation} ({string.Join(", ", details)})";
    }
}
