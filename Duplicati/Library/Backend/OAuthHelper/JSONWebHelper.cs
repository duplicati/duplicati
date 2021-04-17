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
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;

namespace Duplicati.Library
{
    public class JSONWebHelper : IDisposable
    {
        /// <summary>
        /// The User-Agent default value
        /// </summary>
        public static readonly string USER_AGENT = string.Format("Duplicati v{0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
        /// <summary>
        /// The currently set User-Agent value
        /// </summary>
        private readonly string m_user_agent;
        /// <summary>
        /// The URL to perform the OAuth login
        /// </summary>
        public string OAuthLoginUrl { get; protected set; }
        /// <summary>
        /// Gets the current User-Agent value
        /// </summary>
        public string UserAgent { get { return m_user_agent; } }
        /// <summary>
        /// A callback method to help set up the request
        /// </summary>
        public event Action<HttpRequestMessage> CreateSetupHelper;
        /// <summary>
        /// The internal client used to perform the requests
        /// </summary>
        protected readonly HttpClient m_client = new HttpClient();

        /// <summary>
        /// Constructs a new JSONWebHelper
        /// </summary>
        /// <param name="useragent">The User-Agent string to use</param>
        public JSONWebHelper(string useragent = null)
        {
            m_user_agent = useragent ?? USER_AGENT;
        }

        /// <summary>
        /// Method used to create the request
        /// </summary>
        /// <param name="url">The target url</param>
        /// <param name="method">The method to use</param>
        /// <returns>A created request</returns>
        public virtual Task<HttpRequestMessage> CreateRequestAsync(string url, string method, CancellationToken cancelToken)
        {
            var req = new HttpRequestMessage(new HttpMethod(method ?? "GET"), url);
            req.Headers.UserAgent.Clear();
            req.Headers.UserAgent.ParseAdd(UserAgent);

            if (CreateSetupHelper != null)
                CreateSetupHelper(req);

            return Task.FromResult(req);
        }

        /// <summary>
        /// Performs a multipart post and parses the response as JSON
        /// </summary>
        /// <returns>The parsed JSON item.</returns>
        /// <param name="url">The url to post to.</param>
        /// <param name="setup">The optional setup callback method.</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <param name="parts">The multipart items.</param>
        /// <typeparam name="T">The return type parameter.</typeparam>
        public virtual async Task<T> PostMultipartAndGetJSONDataAsync<T>(string url, Action<HttpRequestMessage> setup, CancellationToken cancelToken, params MultipartItem[] parts)
        {
            var response = await PostMultipartAsync(url, setup, cancelToken, parts).ConfigureAwait(false);
            return await ReadJSONResponseAsync<T>(response, cancelToken);
        }

        /// <summary>
        /// List of protected headers that need special handling to se
        /// </summary>
        /// <param name="k">The header name</param>
        /// <returns><c>true</c> if the header is protected; <c>false</c> otherwise</returns>
        private static bool IsProtectedHeader(string k)
            => new [] { "Content-Type", "Content-Disposition", "Content-Length" }
                .Any(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Performs a multipart post
        /// </summary>
        /// <returns>The response.</returns>
        /// <param name="url">The url to post to.</param>
        /// <param name="parts">The multipart items.</param>
        /// <param name="setup">The optional setup callback method.</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        public virtual async Task<HttpResponseMessage> PostMultipartAsync(string url, Action<HttpRequestMessage> setup, CancellationToken cancelToken, params MultipartItem[] parts)
        {
            using(var mpdata = new MultipartFormDataContent(CreateBoundary()))
            {
                foreach(var p in parts)
                {
                    var sc = new StreamContent(p.ContentData);
                    foreach(var h in p.Headers)
                        if (!IsProtectedHeader(h.Key))
                            sc.Headers.Add(h.Key, h.Value);

                    if (p.ContentLength >= 0)
                        sc.Headers.ContentLength = p.ContentLength;

                    if (!string.IsNullOrWhiteSpace(p.ContentTypeName))
                        sc.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(p.Headers["Content-Disposition"]);

                    if (!string.IsNullOrWhiteSpace(p.ContentType))
                        sc.Headers.ContentType = MediaTypeHeaderValue.Parse(p.ContentType);

                    mpdata.Add(sc);
                }

                var req = await CreateRequestAsync(url, "POST", cancelToken);
                setup?.Invoke(req);

                req.Content = mpdata;

                return await m_client.SendAsync(req, cancelToken);
            }
        }

        /// <summary>
        /// Creates a random form bondary string
        /// </summary>
        private static string CreateBoundary()
            => "----DuplicatiFormBoundary" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// Executes a web request and json-deserializes the results as the specified type
        /// </summary>
        /// <returns>The deserialized JSON data.</returns>
        /// <param name="url">The remote URL</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <param name="setup">A callback method that can be used to customize the request, e.g. by setting the method, content-type and headers.</param>
        /// <param name="setupbodyreq">A callback method that can be used to submit data into the body of the request.</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        public virtual Task<T> GetJSONDataAsync<T>(string url, CancellationToken cancelToken)
            => GetJSONDataAsync<T>(url, null, cancelToken);

        /// <summary>
        /// Executes a web request and json-deserializes the results as the specified type
        /// </summary>
        /// <returns>The deserialized JSON data.</returns>
        /// <param name="url">The remote URL</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <param name="setup">A callback method that can be used to customize the request, e.g. by setting the method, content-type and headers.</param>
        /// <param name="setupbodyreq">A callback method that can be used to submit data into the body of the request.</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        public virtual async Task<T> GetJSONDataAsync<T>(string url, Action<HttpRequestMessage> setup, CancellationToken cancelToken)
        {
            var req = await CreateRequestAsync(url, null, cancelToken);
            setup?.Invoke(req);

            return await ReadJSONResponseAsync<T>(req, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a web request by POST'ing the supplied object and json-deserializes the results as the specified type
        /// </summary>
        /// <returns>The deserialized JSON data.</returns>
        /// <param name="url">The remote URL</param>
        /// <param name="item">The data to json-serialize and POST in the request</param>
        /// <param name="method">Alternate HTTP method to use</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        public virtual Task<T> PostAndGetJSONDataAsync<T>(string url, object item, string method, CancellationToken cancelToken)
        {
            var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item));

            return GetJSONDataAsync<T>(
                url,
                req =>
                {                    
                    req.Method = new HttpMethod(method ?? "POST");
                    var sc = new StreamContent(new MemoryStream(data));
                    sc.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8"); 
                    sc.Headers.ContentLength = data.Length;
                    req.Content = sc;
                },
                cancelToken
            );
        }

        /// <summary>
        /// Performs a POST request with the given data, serialized as JSON, and returns the deserialized JSON result
        /// </summary>
        /// <param name="url">The remote URL</param>
        /// <param name="requestdata">The JSON object</param>
        /// <param name="method">The alternate method</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        /// <returns>The deserialized JSON data.</returns>
        public virtual Task<T> ReadJSONResponseAsync<T>(string url, object requestdata, string method, CancellationToken cancelToken)
        {
            if (requestdata is string)
                throw new ArgumentException("Cannot send string object as data");
            
            if (method == null && requestdata != null)
                method = "POST";
                
            return PostAndGetJSONDataAsync<T>(url, requestdata, method, cancelToken);
        }

        /// <summary>
        /// Performs the request and returns the deserialized JSON result
        /// </summary>
        /// <param name="req"></param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        /// <returns>The deserialized JSON data.</returns>
        public virtual async Task<T> ReadJSONResponseAsync<T>(HttpRequestMessage req, CancellationToken cancelToken)
            => await ReadJSONResponseAsync<T>(await m_client.SendAsync(req, cancelToken), cancelToken);

        /// <summary>
        /// Extracts the JSON data an deserializes it, handling invalid JSON data with an improved exception message
        /// </summary>
        /// <param name="resp">The response to read from</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        /// <returns>The deserialized JSON data.</returns>
        public virtual async Task<T> ReadJSONResponseAsync<T>(HttpResponseMessage resp, CancellationToken cancelToken)
        {
            using(var ps = new StreamPeekReader(await resp.Content.ReadAsStreamAsync()))
            {
                try
                {
                    using (var tr = new System.IO.StreamReader(ps))
                    using (var jr = new Newtonsoft.Json.JsonTextReader(tr))
                        return new Newtonsoft.Json.JsonSerializer().Deserialize<T>(jr);
                }
                catch (Exception ex)
                {
                    // If we get invalid JSON, report the peek value
                    if (ex is Newtonsoft.Json.JsonReaderException)
                        throw new IOException($"Invalid JSON data: \"{ps.PeekData()}\"", ex);
                    // Otherwise, we have no additional help to offer
                    throw;
                }
            }
        }

        /// <summary>
        /// Use this method to register an exception handler,
        /// which can throw another, more meaningful exception
        /// </summary>
        /// <param name="ex">The exception being processed.</param>
        protected virtual Task ParseExceptionAsync(Exception ex)
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// Performs a POST request with the given data, response even if the error code is an error
        /// </summary>
        /// <param name="url">The remote URL</param>
        /// <param name="requestdata">The JSON object</param>
        /// <param name="method">The alternate method</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>The response.</returns>
        public async Task<HttpResponseMessage> GetResponseWithoutExceptionAsync(string url, object requestdata, string method, CancellationToken cancelToken)
        {
            if (requestdata is string)
                throw new ArgumentException("Cannot send string object as data");

            if (method == null && requestdata != null)
                method = "POST";

            return await GetResponseWithoutExceptionAsync(await CreateRequestAsync(url, method, cancelToken), requestdata, cancelToken);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="req">The request to execute</param>
        /// <param name="requestdata">The content, either a stream or an object</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>The response.</returns>
        public async Task<HttpResponseMessage> GetResponseWithoutExceptionAsync(HttpRequestMessage req, object requestdata, CancellationToken cancelToken)
        {
            if (requestdata != null)
            {
                if (requestdata is Stream stream)
                {
                    var sc = new StreamContent(stream);
                    sc.Headers.ContentLength = stream.Length;
                    sc.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                    req.Content = sc;
                }
                else
                {
                    var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestdata));
                    var sc = new StreamContent(new MemoryStream(data));
                    sc.Headers.ContentLength = data.Length;

                    sc.Headers.ContentLength  = data.Length;
                    sc.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=UTF-8");

                    req.Content = sc;
                }
            }

            return await m_client.SendAsync(req, cancelToken);
        }

        public async Task<HttpResponseMessage> GetResponseAsync(string url, object requestdata, string method, CancellationToken cancelToken)
        {
            if (requestdata is string)
                throw new ArgumentException("Cannot send string object as data");

            if (method == null && requestdata != null)
                method = "POST";

            return await GetResponseAsync(await CreateRequestAsync(url, method, cancelToken), requestdata, cancelToken);
        }

        public async Task<HttpResponseMessage> GetResponseAsync(HttpRequestMessage req, object requestdata, CancellationToken cancelToken)
        {
            try
            {
                var resp = await GetResponseWithoutExceptionAsync(req, requestdata, cancelToken);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestStatusException(resp);
                resp.EnsureSuccessStatusCode();
                return resp;
            }
            catch (Exception ex)
            {
                await ParseExceptionAsync(ex);
                throw;
            }
        }

        public void Dispose()
        {
            m_client.Dispose();
        }

        public class HttpRequestStatusException : HttpRequestException
        {
            public readonly HttpResponseMessage Response;

            public HttpRequestStatusException(HttpResponseMessage resp)
                : base(resp.ReasonPhrase)
            {
                Response = resp;
            }
        }

        /// <summary>
        /// A utility class that shadows the real stream but provides access
        /// to the first 2kb of the stream to use in error reporting
        /// </summary>
        protected class StreamPeekReader : Stream
        {
            private readonly Stream m_base;
            private readonly byte[] m_peekbuffer = new byte[1024 * 2];
            private int m_peekbytes = 0;

            public StreamPeekReader(Stream source)
            {
                m_base = source;
            }

            public string PeekData()
            {
                if (m_peekbuffer.Length == 0)
                    return string.Empty;

                return Encoding.UTF8.GetString(m_peekbuffer, 0, m_peekbytes);
            }

            public override bool CanRead => m_base.CanRead;
            public override bool CanSeek => m_base.CanSeek;
            public override bool CanWrite => m_base.CanWrite;
            public override long Length => m_base.Length;
            public override long Position { get => m_base.Position; set => m_base.Position = value; }
            public override void Flush() => m_base.Flush();
            public override long Seek(long offset, SeekOrigin origin) => m_base.Seek(offset, origin);
            public override void SetLength(long value) => m_base.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => m_base.Write(buffer, offset, count);
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) => m_base.BeginRead(buffer, offset, count, callback, state);
            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state) => m_base.BeginWrite(buffer, offset, count, callback, state);
            public override bool CanTimeout => m_base.CanTimeout;
            public override void Close() => m_base.Close();
            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => m_base.CopyToAsync(destination, bufferSize, cancellationToken);
            protected override void Dispose(bool disposing) => base.Dispose(disposing);
            public override int EndRead(IAsyncResult asyncResult) => m_base.EndRead(asyncResult);
            public override void EndWrite(IAsyncResult asyncResult) => m_base.EndWrite(asyncResult);
            public override Task FlushAsync(CancellationToken cancellationToken) => m_base.FlushAsync(cancellationToken);
            public override int ReadTimeout { get => m_base.ReadTimeout; set => m_base.ReadTimeout = value; }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => m_base.WriteAsync(buffer, offset, count, cancellationToken);
            public override int WriteTimeout { get => m_base.WriteTimeout; set => m_base.WriteTimeout = value; }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var br = 0;
                if (m_peekbytes < m_peekbuffer.Length - 1)
                {
                    var maxb = Math.Min(count, m_peekbuffer.Length - m_peekbytes);
                    br = await m_base.ReadAsync(m_peekbuffer, m_peekbytes, maxb, cancellationToken);
                    Array.Copy(m_peekbuffer, m_peekbytes, buffer, offset, br);
                    m_peekbytes += br;
                    offset += br;
                    count -= br;
                    if (count == 0 || br < maxb)
                        return br;
                }

                return await m_base.ReadAsync(buffer, offset, count, cancellationToken) + br;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var br = 0;
                if (m_peekbytes < m_peekbuffer.Length - 1)
                {
                    var maxb = Math.Min(count, m_peekbuffer.Length - m_peekbytes);
                    br = m_base.Read(m_peekbuffer, m_peekbytes, maxb);
                    Array.Copy(m_peekbuffer, m_peekbytes, buffer, offset, br);
                    m_peekbytes += br;
                    offset += br;
                    count -= br;

                    if (count == 0 || br < maxb)
                        return br;
                }

                return m_base.Read(buffer, offset, count) + br;
            }

        }
    }
}
