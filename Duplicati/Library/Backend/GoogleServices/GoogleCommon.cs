//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using Duplicati.Library.Utility;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

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
        private static long QueryUploadRange<T>(OAuthHelper oauth, string uploaduri, long streamlength, out T response)
            where T : class
        {
            response = null;
            HttpWebRequest req = oauth.CreateRequest(uploaduri);
            req.Method = "PUT";
            req.ContentLength = 0;
            req.Headers["Content-Range"] = string.Format("bytes */{0}", streamlength);

            AsyncHttpRequest areq = new AsyncHttpRequest(req);
            using(HttpWebResponse resp = oauth.GetResponseWithoutException(areq))
            {
                int code = (int)resp.StatusCode;

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
                        return long.Parse(resp.Headers["Range"].Split(new char[] { '-' })[1]) + 1;
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
        /// <param name="method">The HTTP Method.</param>
        /// <typeparam name="TRequest">The type of data to upload as metadata.</typeparam>
        /// <typeparam name="TResponse">The type of data returned from the upload.</typeparam>
        public static async Task<TResponse> ChunkedUploadWithResumeAsync<TRequest, TResponse>(OAuthHelper oauth, TRequest requestdata, string url, System.IO.Stream stream, CancellationToken cancelToken, string method = "POST")
            where TRequest : class
            where TResponse : class
        {
            string uploaduri;

            byte[] data = requestdata == null ? null : System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestdata));

            HttpWebRequest req = oauth.CreateRequest(url);
            req.Method = method;
            req.ContentLength = data == null ? 0 : data.Length;

            if (data != null)
                req.ContentType = "application/json; charset=UTF-8";
            
            req.Headers["X-Upload-Content-Type"] = "application/octet-stream";
            req.Headers["X-Upload-Content-Length"] = stream.Length.ToString();

            try
            {
                AsyncHttpRequest areq = new AsyncHttpRequest(req);
                if (data != null)
                    using (Stream rs = areq.GetRequestStream())
                        await rs.WriteAsync(data, 0, data.Length, cancelToken).ConfigureAwait(false);

                using (HttpWebResponse resp = (HttpWebResponse)areq.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(resp.Headers["Location"]))
                        throw new WebException("Failed to start upload session", null, WebExceptionStatus.UnknownError, resp);

                    uploaduri = resp.Headers["Location"];
                }
            }
            /*
             * Catch any web exceptions and grab the error detail from the repose stream
             */
            catch (WebException wex)
            {
                using (Stream exstream = wex.Response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(exstream))
                    {
                        string error = reader.ReadToEnd();

                        throw new WebException(error, wex, wex.Status, wex.Response);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }

            return await ChunkedUploadAsync<TResponse>(oauth, uploaduri, stream, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Helper method that performs a chunked upload, and queries for http status after each chunk
        /// </summary>
        /// <returns>The response item</returns>
        /// <param name="oauth">The Oauth instance</param>
        /// <param name="uploaduri">The resumeable uploaduri</param>
        /// <param name="stream">The stream with data to upload.</param>
        /// <typeparam name="T">The type of data in the response.</typeparam>
        private static async Task<T> ChunkedUploadAsync<T>(OAuthHelper oauth, string uploaduri, System.IO.Stream stream, CancellationToken cancelToken)
            where T : class
        {
            bool queryRange = false;
            int retries = 0;
            long offset = 0L;
            byte[] buffer = new byte[Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

            // Repeatedly try uploading until all retries are done
            while(true)
            {
                try
                {
                    if (queryRange)
                    {
                        T re;
                        offset = GoogleCommon.QueryUploadRange(oauth, uploaduri, stream.Length, out re);
                        queryRange = false;

                        if (re != null)
                            return re;
                    }

                    //Seek into the right place
                    if (stream.Position != offset)
                        stream.Position = offset;

                    HttpWebRequest req = oauth.CreateRequest(uploaduri);
                    req.Method = "PUT";
                    req.ContentType = "application/octet-stream";

                    long chunkSize = Math.Min(UPLOAD_CHUNK_SIZE, stream.Length - offset);

                    req.ContentLength = chunkSize;
                    req.Headers["Content-Range"] = string.Format("bytes {0}-{1}/{2}", offset, offset + chunkSize - 1, stream.Length);

                    // Upload the remaining data
                    AsyncHttpRequest areq = new AsyncHttpRequest(req);
                    using(Stream rs = areq.GetRequestStream())
                    {
                        long remaining = chunkSize;
                        while(remaining > 0)
                        {
                            int n = stream.Read(buffer, 0, (int)Math.Min(remaining, Library.Utility.Utility.DEFAULT_BUFFER_SIZE));
                            await rs.WriteAsync(buffer, 0, n, cancelToken);
                            remaining -= n;
                        }
                    }

                    // Check the response
                    using(HttpWebResponse resp = oauth.GetResponseWithoutException(areq))
                    {
                        int code = (int)resp.StatusCode;

                        if (code == 308 && resp.Headers["Range"] != null)
                        {
                            offset = long.Parse(resp.Headers["Range"].Split(new char[] {'-'})[1]) + 1;
                            retries = 0;
                        }
                        else if (code >= 200 && code <= 299)
                        {
                            offset += chunkSize;
                            if (offset != stream.Length)
                                throw new Exception(string.Format("Upload succeeded prematurely. Uploaded: {0}, total size: {1}", offset, stream.Length));

                            //Verify that the response is also valid
                            T res = oauth.ReadJSONResponse<T>(resp);
                            if (res == null)
                                throw new Exception(string.Format("Upload succeeded, but no data was returned, status code: {0}", code));

                            return res;
                        }
                        else
                        {
                            /*
                             * Grab the error detail from the response stream
                             */
                            using (Stream exstream = resp.GetResponseStream())
                            {
                                using (StreamReader reader = new StreamReader(exstream))
                                {
                                    string error = reader.ReadToEnd();
                                    throw new WebException(string.Format("Unexpected status code: {0} with error {1}", code, error), null, WebExceptionStatus.ServerProtocolViolation, resp);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    bool retry = false;

                    // If we get a 5xx error, or some network issue, we retry
                    if (ex is WebException exception && exception.Response is HttpWebResponse response)
                    {
                        int code = (int)response.StatusCode;
                        retry = code >= 500 && code <= 599;
                    }
                    else if (ex is System.Net.Sockets.SocketException || ex is System.IO.IOException || ex.InnerException is System.Net.Sockets.SocketException || ex.InnerException is System.IO.IOException)
                    {
                        retry = true;
                    }

                    // Retry with exponential backoff
                    if (retry && retries < 5)
                    {
                        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                        retries++;

                        // Ask server where we left off
                        queryRange = true;
                    }
                    else
                        throw;
                }
            }
        }
    }
}

