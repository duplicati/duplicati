// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Headers;
using Duplicati.Library.Utility;
using Google.Apis.Requests;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Reads content from Google, so every entry that downloads content applies the same rules:
/// the content is read as it arrives, never held in memory as a whole, and a download that
/// did not complete is a failure, never empty or truncated content
/// </summary>
internal static class GoogleDownloads
{
    /// <summary>
    /// Opens the media a service request addresses, such as <c>Files.Get</c> with
    /// <c>alt=media</c> or <c>Files.Export</c>, as a stream read straight from the
    /// connection. The request only supplies the URL, built by the client library, and the
    /// client of its service, which carries the credentials; it is never executed itself.
    /// </summary>
    /// <param name="request">The request addressing the media</param>
    /// <param name="expectedLength">The length from the listing, or <c>null</c> if the listing gave none</param>
    /// <param name="readTimeout">How long a read may wait for bytes before the connection is taken as stalled and the download resumed</param>
    /// <param name="description">What is downloaded, for log and error messages</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content stream, opened so a refused request fails here</returns>
    public static Task<Stream> OpenMediaAsync(IClientServiceRequest request, long? expectedLength, TimeSpan readTimeout, string description, CancellationToken cancellationToken)
    {
        using var message = request.CreateRequest();
        var url = message.RequestUri?.ToString() ?? throw new InvalidOperationException($"The request for {description} has no URL");
        var service = request.Service;

        // A refused request is reported the way the client library reports one, with the
        // error Google sent, so the callers see the same exception as for any other request
        async Task<Exception> toError(HttpResponseMessage response, CancellationToken ct)
        {
            // The body is buffered first, by the retry handler or here, so it can be read again
            // after the library has read it
            if (response.Content != null)
            {
                try
                {
                    await response.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The body is gone; the status code is all there is
                }
            }

            Google.Apis.Requests.RequestError? error = null;
            try
            {
                error = await service.DeserializeError(response).ConfigureAwait(false);
            }
            catch (Google.GoogleApiException)
            {
                // The library throws for a body that is not the error object it expects, as a
                // media endpoint sends; the body is parsed below instead
            }

            if (string.IsNullOrWhiteSpace(error?.Message))
            {
                // A media endpoint wraps the usual error object in a one-element array, which
                // the library's parser does not look into; the message is taken from there
                var content = error?.ErrorResponseContent;
                if (string.IsNullOrWhiteSpace(content) && response.Content != null)
                {
                    try
                    {
                        content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // The body is gone; the status code is all there is
                    }
                }

                var wrapped = ParseWrappedError(content);
                if (wrapped != null)
                {
                    error ??= new Google.Apis.Requests.RequestError { Code = (int)response.StatusCode };
                    error.Message = wrapped;
                }
            }

            return new Google.GoogleApiException(service.Name, error?.ToString() ?? $"The request for {description} failed with status {(int)response.StatusCode} {response.StatusCode}")
            {
                Error = error,
                HttpStatusCode = response.StatusCode,
            };
        }

        return OpenMediaAsync(service.HttpClient, url, toError, expectedLength, readTimeout, description, cancellationToken);
    }

    /// <summary>
    /// Reads the message out of an error document wrapped in a JSON array, as the media
    /// endpoints answer: <c>[{"error": {"message": "...", "errors": [{"reason": "..."}]}}]</c>
    /// </summary>
    /// <param name="content">The body of the response</param>
    /// <returns>The message, with the reasons when there are any, or <c>null</c> if the body is not such a document</returns>
    private static string? ParseWrappedError(string? content)
    {
        if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith('['))
            return null;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(content);
            var first = document.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != System.Text.Json.JsonValueKind.Object
                || !first.TryGetProperty("error", out var error)
                || !error.TryGetProperty("message", out var message)
                || message.ValueKind != System.Text.Json.JsonValueKind.String
                || string.IsNullOrWhiteSpace(message.GetString()))
                return null;

            var reasons = error.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Array
                ? errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("reason", out var reason) && reason.ValueKind == System.Text.Json.JsonValueKind.String ? reason.GetString() : null)
                    .Where(r => !string.IsNullOrEmpty(r))
                    .Distinct()
                    .ToList()
                : [];

            return reasons.Count == 0 ? message.GetString() : $"{message.GetString()} (reason: {string.Join(", ", reasons)})";
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the media at a URL as a stream read straight from the connection, for content
    /// of any size, such as the binary content of a Drive file or one of its revisions. The
    /// content is never held in memory as a whole: the backup reads it as it arrives. A
    /// connection that fails or stalls is resumed from where reading stopped with a range
    /// request, through the same client, so the credentials are refreshed if the token
    /// expired meanwhile. A download that cannot be completed fails, never ends early.
    /// </summary>
    /// <remarks>
    /// The backup needs the length of what it reads, for its progress, and the engine warns
    /// about a stream that cannot give one. The length comes from the listing or from the
    /// <c>Content-Length</c> header for all content of any size: a file in a drive always
    /// lists its size. Nothing states the length of an export, which Google generates on the
    /// fly, or of some attachments; those are read into a temporary file first, so the length
    /// is known, and they are the small kind, as an export is capped by Google.
    /// </remarks>
    /// <param name="client">The client of the service the media belongs to, which carries the credentials</param>
    /// <param name="url">The media URL</param>
    /// <param name="expectedLength">The length from the listing, or <c>null</c> if the listing gave none</param>
    /// <param name="readTimeout">How long a read may wait for bytes before the connection is taken as stalled and the download resumed; the Google client's own timeout covers only the response headers once those are read, so without this a connection that goes quiet in the middle of a body would hang the backup</param>
    /// <param name="description">What is downloaded, for log and error messages</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content stream, opened so a refused request fails here</returns>
    public static Task<Stream> OpenMediaAsync(HttpClient client, string url, long? expectedLength, TimeSpan readTimeout, string description, CancellationToken cancellationToken)
        => OpenMediaAsync(client, url, null, expectedLength, readTimeout, description, cancellationToken);

    /// <summary>
    /// Opens the media at a URL, see <see cref="OpenMediaAsync(HttpClient, string, long?, TimeSpan, string, CancellationToken)"/>
    /// </summary>
    /// <param name="client">The client of the service the media belongs to, which carries the credentials</param>
    /// <param name="url">The media URL</param>
    /// <param name="toError">Turns a response that is not successful into the exception to throw, or <c>null</c> to throw an <see cref="HttpRequestException"/> with the status</param>
    /// <param name="expectedLength">The length from the listing, or <c>null</c> if the listing gave none</param>
    /// <param name="readTimeout">How long a read may wait for bytes before the connection is taken as stalled and the download resumed</param>
    /// <param name="description">What is downloaded, for log and error messages</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content stream, opened so a refused request fails here</returns>
    private static async Task<Stream> OpenMediaAsync(HttpClient client, string url, Func<HttpResponseMessage, CancellationToken, Task<Exception>>? toError, long? expectedLength, TimeSpan readTimeout, string description, CancellationToken cancellationToken)
    {
        var stream = new ResumableHttpReadStream(async (offset, ct) =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (offset > 0)
                request.Headers.Range = new RangeHeaderValue(offset, null);

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode || toError == null)
                return response;

            using (response)
                throw await toError(response, ct).ConfigureAwait(false);
        }, expectedLength, readTimeout, description);

        try
        {
            await stream.EnsureOpenedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        if (stream.HasKnownLength)
        {
            if (stream.Length == 0)
            {
                stream.Dispose();
                return Stream.Null;
            }

            return stream;
        }

        // Nothing states the length, so it is only known once the content is read
        using (stream)
        {
            var tempStream = TempFileStream.Create();
            try
            {
                await stream.CopyToAsync(tempStream, cancellationToken).ConfigureAwait(false);
                tempStream.Position = 0;
            }
            catch
            {
                tempStream.Dispose();
                throw;
            }

            // Deleted when the backup disposes it
            return tempStream;
        }
    }
}
