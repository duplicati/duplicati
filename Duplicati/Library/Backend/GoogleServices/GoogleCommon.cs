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

using Duplicati.Library.Utility;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Duplicati.Library.Backend.GoogleServices
{
    internal static class GoogleCommon
    {
        /// <summary>
        /// The size of upload chunks
        /// </summary>
        private const long UPLOAD_CHUNK_SIZE = 1024 * 1024 * 10;

        /// <summary>
        /// Helper method that queries a resumeable upload uri for progress
        /// </summary>
        /// <typeparam name="T">The type of data in the response.</typeparam>
        /// <param name="oauth">The Oauth instance</param>
        /// <param name="uploaduri">The resumeable uploaduri</param>
        /// <param name="streamlength">The length of the entire stream</param>
        /// <param name="shortTimeout">The short request timeout.</param>
        /// <param name="readWriteTimeout">The read write timeout.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Tuple with the range and the response.</returns>
        private static async Task<(long, T? Response)> QueryUploadRange<T>(JsonWebHelperHttpClient oauth, string uploaduri, long streamlength, TimeSpan shortTimeout, TimeSpan readWriteTimeout, CancellationToken cancellationToken)
            where T : class
        {
            using var req = await oauth.CreateRequestAsync(uploaduri, HttpMethod.Put, cancellationToken);
            req.Content = new ByteArrayContent(Array.Empty<byte>());
            req.Content.Headers.Add("Content-Range", $"bytes */{streamlength}");

            using var resp = await Library.Utility.Utility.WithTimeout(
                shortTimeout,
                cancellationToken,
                ct => oauth.GetResponseAsync(req, HttpCompletionOption.ResponseContentRead, ct)
            ).ConfigureAwait(false);

            var code = (int)resp.StatusCode;

            // If the upload is completed, we get 201 or 200
            if (code >= 200 && code <= 299)
            {
                // No timeout here, beause the response content is already read
                var response = await resp.Content.ReadFromJsonAsync<T>(cancellationToken);
                if (response == null)
                    throw new Exception(string.Format("Upload succeeded, but no data was returned, status code: {0}", code));

                return (streamlength, response);
            }

            if (code == 308)
            {
                // A lack of a Range header is undocumented, 
                // but seems to occur when no data has reached the server:
                // https://code.google.com/a/google.com/p/apps-api-issues/issues/detail?id=3884

                if (!resp.Content.Headers.TryGetValues("Range", out var rangeValues))
                    return (0, null);

                return (long.Parse(rangeValues.First().Split('-')[1]) + 1, null);
            }
            else
                throw new HttpRequestException(HttpRequestError.HttpProtocolError, string.Format("Unexpected status code: {0}", code), null, resp.StatusCode);

        }

        /// <summary>
        /// Uploads the requestdata as JSON and starts a resumeable upload session, then uploads the stream in chunks
        /// </summary>
        /// <returns>Serialized result of the last request.</returns>
        /// <param name="oauth">The Oauth instance.</param>
        /// <param name="requestdata">The data to submit as JSON metadata.</param>
        /// <param name="url">The URL to register the upload session against.</param>
        /// <param name="stream">The stream with content data to upload.</param>
        /// <param name="shortTimeout">The short request timeout.</param>
        /// <param name="readWriteTimeout">The read write timeout.</param>
        /// <param name="method">The HTTP Method.</param>
        /// <typeparam name="TRequest">The type of data to upload as metadata.</typeparam>
        /// <typeparam name="TResponse">The type of data returned from the upload.</typeparam>
        public static async Task<TResponse> ChunkedUploadWithResumeAsync<TRequest, TResponse>(JsonWebHelperHttpClient oauth, TRequest requestdata, string url, Stream stream, TimeSpan shortTimeout, TimeSpan readWriteTimeout, CancellationToken cancelToken, HttpMethod method)
            where TRequest : class
            where TResponse : class
        {
            using var req = await oauth.CreateRequestAsync(url, method, cancelToken);
            if (requestdata != null)
                req.Content = JsonContent.Create(requestdata);
            req.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
            req.Headers.Add("X-Upload-Content-Length", stream.Length.ToString());

            var uploaduri = await Utility.Utility.WithTimeout(shortTimeout, cancelToken, async ct =>
            {
                using var resp = await oauth.GetResponseAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                if (!resp.Headers.TryGetValues("Location", out var locationValues) || string.IsNullOrWhiteSpace(locationValues.FirstOrDefault()))
                    throw new HttpRequestException(HttpRequestError.Unknown, "Failed to start upload session", null, resp.StatusCode);

                return locationValues.First();
            }).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(uploaduri))
                throw new Exception("Failed to start upload session");

            return await ChunkedUploadAsync<TResponse>(oauth, uploaduri, stream, shortTimeout, readWriteTimeout, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Helper method that performs a chunked upload, and queries for http status after each chunk
        /// </summary>
        /// <returns>The response item</returns>
        /// <param name="oauth">The Oauth instance</param>
        /// <param name="uploaduri">The resumeable uploaduri</param>
        /// <param name="stream">The stream with data to upload.</param>
        /// <param name="shortTimeout">The short request timeout.</param>
        /// <param name="readWriteTimeout">The read write timeout.</param>
        /// <typeparam name="T">The type of data in the response.</typeparam>
        private static async Task<T> ChunkedUploadAsync<T>(JsonWebHelperHttpClient oauth, string uploaduri, Stream stream, TimeSpan shortTimeout, TimeSpan readWriteTimeout, CancellationToken cancelToken)
            where T : class
        {
            var queryRange = false;
            var retries = 0;
            var offset = 0L;

            // Repeatedly try uploading until all retries are done
            while (true)
            {
                try
                {
                    if (queryRange)
                    {
                        (offset, var re) = await QueryUploadRange<T>(oauth, uploaduri, stream.Length, shortTimeout, readWriteTimeout, cancelToken).ConfigureAwait(false);
                        queryRange = false;

                        if (re != null)
                            return re;
                    }

                    //Seek into the right place
                    if (stream.Position != offset)
                        stream.Position = offset;

                    using var req = await oauth.CreateRequestAsync(uploaduri, HttpMethod.Put, cancelToken);

                    var chunkSize = Math.Min(UPLOAD_CHUNK_SIZE, stream.Length - offset);
                    using var ls = new ReadLimitLengthStream(stream, offset, chunkSize);
                    using var ts = ls.ObserveWriteTimeout(readWriteTimeout);
                    req.Content = new StreamContent(ts);
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    req.Content.Headers.Add("Content-Range", $"bytes {offset}-{offset + chunkSize - 1}/{stream.Length}");
                    using var resp = await oauth.GetResponseUncheckedAsync(req, HttpCompletionOption.ResponseContentRead, cancelToken).ConfigureAwait(false);

                    // Check the response
                    var code = (int)resp.StatusCode;

                    if ((int)resp.StatusCode == 308 &&
                        resp.Headers.TryGetValues("Range", out var rangeValues) &&
                        !string.IsNullOrWhiteSpace(rangeValues.FirstOrDefault()))
                    {
                        offset = long.Parse(rangeValues.First().Split('-')[1]) + 1;
                        retries = 0;
                    }
                    else if (code >= 200 && code <= 299)
                    {
                        offset += chunkSize;
                        if (offset != stream.Length)
                            throw new HttpRequestException(HttpRequestError.HttpProtocolError, string.Format("Upload succeeded prematurely. Uploaded: {0}, total size: {1}", offset, stream.Length), null, resp.StatusCode);

                        //Verify that the response is also valid (no timeout guard, as we already read the content)
                        return await resp.Content.ReadFromJsonAsync<T>(cancelToken).ConfigureAwait(false)
                            ?? throw new HttpRequestException(HttpRequestError.HttpProtocolError, string.Format("Upload succeeded, but no data was returned, status code: {0}", code), null, resp.StatusCode);
                    }
                    else
                    {
                        throw new HttpRequestException(HttpRequestError.HttpProtocolError, string.Format("Unexpected status code: {0}", code), null, resp.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    var retry = false;

                    // Check for HttpRequestException and inspect status code if available
                    if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
                    {
                        var code = (int)httpEx.StatusCode.Value;
                        retry = code >= 500 && code <= 599;
                    }
                    else if (ex is IOException ||
                             ex is System.Net.Sockets.SocketException ||
                             ex.InnerException is IOException ||
                             ex.InnerException is System.Net.Sockets.SocketException)
                    {
                        retry = true;
                    }

                    // Retry with exponential backoff
                    if (retry && retries < 5)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                        retries++;

                        // Ask server where we left off
                        queryRange = true;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
    }
}

