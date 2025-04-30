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
        /// <returns>The upload range.</returns>
        /// <param name="oauth">The Oauth instance</param>
        /// <param name="uploaduri">The resumeable uploaduri</param>
        /// <param name="streamlength">The length of the entire stream</param>
        private static long QueryUploadRange<T>(OAuthHelper oauth, string uploaduri, long streamlength, out T? response)
            where T : class
        {
            response = null;
            var req = oauth.CreateRequest(uploaduri);
            req.Method = "PUT";
            req.ContentLength = 0;
            req.Headers["Content-Range"] = string.Format("bytes */{0}", streamlength);

            // TODO: Apply timeout when this is upgraded to HttpClient
            var areq = new AsyncHttpRequest(req);
            using (var resp = oauth.GetResponseWithoutException(areq))
            {
                var code = (int)resp.StatusCode;

                // If the upload is completed, we get 201 or 200
                if (code >= 200 && code <= 299)
                {
                    response = oauth.ReadJSONResponse<T>(resp);
                    if (response == null)
                        throw new Exception(string.Format("Upload succeeded, but no data was returned, status code: {0}", code));

                    return streamlength;
                }

                if (code == 308)
                {
                    // A lack of a Range header is undocumented, 
                    // but seems to occur when no data has reached the server:
                    // https://code.google.com/a/google.com/p/apps-api-issues/issues/detail?id=3884

                    if (resp.Headers["Range"] == null)
                        return 0;
                    else
                        return long.Parse(resp.Headers["Range"]!.Split(new char[] { '-' })[1]) + 1;
                }
                else
                    throw new WebException(string.Format("Unexpected status code: {0}", code), null, WebExceptionStatus.ServerProtocolViolation, resp);
            }
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
        public static async Task<TResponse> ChunkedUploadWithResumeAsync<TRequest, TResponse>(OAuthHelper oauth, TRequest requestdata, string url, Stream stream, TimeSpan shortTimeout, TimeSpan readWriteTimeout, CancellationToken cancelToken, string method = "POST")
            where TRequest : class
            where TResponse : class
        {
            var data = requestdata == null ? null : System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestdata));

            var req = oauth.CreateRequest(url);
            req.Method = method;
            req.ContentLength = data == null ? 0 : data.Length;

            if (data != null)
                req.ContentType = "application/json; charset=UTF-8";

            req.Headers["X-Upload-Content-Type"] = "application/octet-stream";
            req.Headers["X-Upload-Content-Length"] = stream.Length.ToString();

            var uploaduri = await Utility.Utility.WithTimeout(shortTimeout, cancelToken, async ct =>
            {
                var areq = new AsyncHttpRequest(req);
                if (data != null)
                    using (var rs = areq.GetRequestStream())
                        await rs.WriteAsync(data, 0, data.Length, cancelToken).ConfigureAwait(false);

                using (var resp = (HttpWebResponse)areq.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(resp.Headers["Location"]))
                        throw new WebException("Failed to start upload session", null, WebExceptionStatus.UnknownError, resp);

                    return resp.Headers["Location"];
                }
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
        private static async Task<T> ChunkedUploadAsync<T>(OAuthHelper oauth, string uploaduri, Stream stream, TimeSpan shortTimeout, TimeSpan readWriteTimeout, CancellationToken cancelToken)
            where T : class
        {
            var queryRange = false;
            var retries = 0;
            var offset = 0L;
            var buffer = new byte[Library.Utility.Utility.DEFAULT_BUFFER_SIZE];
            HttpWebResponse? resp = null;

            // Repeatedly try uploading until all retries are done
            while (true)
            {
                try
                {
                    if (queryRange)
                    {
                        offset = QueryUploadRange<T>(oauth, uploaduri, stream.Length, out var re);
                        queryRange = false;

                        if (re != null)
                            return re;
                    }

                    //Seek into the right place
                    if (stream.Position != offset)
                        stream.Position = offset;

                    var req = oauth.CreateRequest(uploaduri);
                    req.Method = "PUT";
                    req.ContentType = "application/octet-stream";

                    var chunkSize = Math.Min(UPLOAD_CHUNK_SIZE, stream.Length - offset);

                    req.ContentLength = chunkSize;
                    req.Headers["Content-Range"] = string.Format("bytes {0}-{1}/{2}", offset, offset + chunkSize - 1, stream.Length);

                    // Upload the remaining data
                    var areq = new AsyncHttpRequest(req);
                    using (var rs = areq.GetRequestStream())
                    using (var ts = rs.ObserveWriteTimeout(readWriteTimeout))
                    {
                        var remaining = chunkSize;
                        while (remaining > 0)
                        {
                            var n = stream.Read(buffer, 0, (int)Math.Min(remaining, Library.Utility.Utility.DEFAULT_BUFFER_SIZE));
                            await ts.WriteAsync(buffer, 0, n, cancelToken);
                            remaining -= n;
                        }
                    }

                    // Check the response
                    resp = await Utility.Utility.WithTimeout(shortTimeout, cancelToken, _ => oauth.GetResponseWithoutException(areq)).ConfigureAwait(false);
                    var code = (int)resp.StatusCode;

                    if (code == 308 && resp.Headers["Range"] != null)
                    {
                        offset = long.Parse(resp.Headers["Range"]!.Split(new char[] { '-' })[1]) + 1;
                        retries = 0;
                    }
                    else if (code >= 200 && code <= 299)
                    {
                        offset += chunkSize;
                        if (offset != stream.Length)
                            throw new Exception(string.Format("Upload succeeded prematurely. Uploaded: {0}, total size: {1}", offset, stream.Length));

                        //Verify that the response is also valid
                        var res = oauth.ReadJSONResponse<T>(resp);
                        if (res == null)
                            throw new Exception(string.Format("Upload succeeded, but no data was returned, status code: {0}", code));

                        return res;
                    }
                    else
                    {
                        throw new WebException(string.Format("Unexpected status code: {0}", code), null, WebExceptionStatus.ServerProtocolViolation, resp);
                    }
                }
                catch (Exception ex)
                {
                    var retry = false;

                    // If we get a 5xx error, or some network issue, we retry
                    if (ex is WebException exception && exception.Response is HttpWebResponse response)
                    {
                        try
                        {
                            var code = (int)response.StatusCode;
                            retry = code >= 500 && code <= 599;
                        }
                        catch
                        {
                            // Assume this is a transient error
                            retry = true;
                        }
                    }
                    else if (ex is System.Net.Sockets.SocketException || ex is IOException || ex.InnerException is System.Net.Sockets.SocketException || ex.InnerException is System.IO.IOException)
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
                        throw;
                }
                finally
                {
                    // Wait until the end of the request to dispose of the response, as it may be needed for error handling
                    resp?.Dispose();
                }
            }
        }
    }
}

