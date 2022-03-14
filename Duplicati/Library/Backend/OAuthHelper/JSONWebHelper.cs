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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library
{
    public class JSONWebHelper
    {
        public static readonly string USER_AGENT = string.Format("Duplicati v{0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
        private readonly string m_user_agent;
        public string OAuthLoginUrl { get; protected set; }
        public string UserAgent { get { return m_user_agent; } }
        public event Action<HttpWebRequest> CreateSetupHelper;
        private static readonly byte[] crlf = Encoding.UTF8.GetBytes("\r\n");

        public JSONWebHelper(string useragent = null)
        {
            m_user_agent = useragent ?? USER_AGENT;
        }

        public virtual HttpWebRequest CreateRequest(string url, string method = null)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            if (method != null)
                req.Method = method;

            if (CreateSetupHelper != null)
                CreateSetupHelper(req);

            return req;
        }

        /// <summary>
        /// Performs a multipart post and parses the response as JSON
        /// </summary>
        /// <returns>The parsed JSON item.</returns>
        /// <param name="url">The url to post to.</param>
        /// <param name="parts">The multipart items.</param>
        /// <typeparam name="T">The return type parameter.</typeparam>
        public virtual T PostMultipartAndGetJSONData<T>(string url, params MultipartItem[] parts)
        {
            return ReadJSONResponse<T>(PostMultipart(url, null, parts));
        }

        /// <summary>
        /// Performs a multipart post and parses the response as JSON
        /// </summary>
        /// <returns>The parsed JSON item.</returns>
        /// <param name="url">The url to post to.</param>
        /// <param name="parts">The multipart items.</param>
        /// <param name="setup">The optional setup callback method.</param>
        /// <typeparam name="T">The return type parameter.</typeparam>
        public virtual T PostMultipartAndGetJSONData<T>(string url, Action<HttpWebRequest> setup = null, params MultipartItem[] parts)
        {
            return ReadJSONResponse<T>(PostMultipart(url, setup, parts));
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
        public virtual async Task<T> PostMultipartAndGetJSONDataAsync<T>(string url, Action<HttpWebRequest> setup, CancellationToken cancelToken, params MultipartItem[] parts)
        {
            var response = await PostMultipartAsync(url, setup, cancelToken, parts).ConfigureAwait(false);
            return ReadJSONResponse<T>(response);
        }

        /// <summary>
        /// Performs a multipart post
        /// </summary>
        /// <returns>The response.</returns>
        /// <param name="url">The url to post to.</param>
        /// <param name="parts">The multipart items.</param>
        /// <param name="setup">The optional setup callback method.</param>
        public virtual HttpWebResponse PostMultipart(string url, Action<HttpWebRequest> setup = null, params MultipartItem[] parts)
        {
            CreateBoundary(out var boundary, out var bodyTerminator);

            var req = PreparePostMultipart(url, setup, boundary, bodyTerminator, out var headers, parts);
            var areq = new AsyncHttpRequest(req);

            using (var rs = areq.GetRequestStream())
            {
                foreach(var p in headers)
                {
                    rs.Write(p.Header, 0, p.Header.Length);
                    Utility.Utility.CopyStream(p.Part.ContentData, rs);
                    rs.Write(crlf, 0, crlf.Length);
                }

                rs.Write(bodyTerminator, 0, bodyTerminator.Length);
            }

            return GetResponse(areq);
        }

        /// <summary>
        /// Performs a multipart post
        /// </summary>
        /// <returns>The response.</returns>
        /// <param name="url">The url to post to.</param>
        /// <param name="parts">The multipart items.</param>
        /// <param name="setup">The optional setup callback method.</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        public virtual async Task<HttpWebResponse> PostMultipartAsync(string url, Action<HttpWebRequest> setup, CancellationToken cancelToken, params MultipartItem[] parts)
        {
            CreateBoundary(out var boundary, out var bodyTerminator);

            var req = PreparePostMultipart(url, setup, boundary, bodyTerminator, out var headers, parts);
            var areq = new AsyncHttpRequest(req);
            var buffer = new byte[Utility.Utility.DEFAULT_BUFFER_SIZE];

            using (var rs = areq.GetRequestStream())
            {
                foreach (var p in headers)
                {
                    await rs.WriteAsync(p.Header, 0, p.Header.Length, cancelToken).ConfigureAwait(false);
                    await Utility.Utility.CopyStreamAsync(p.Part.ContentData, rs, tryRewindSource: true, cancelToken:cancelToken, buf: buffer).ConfigureAwait(false);
                    await rs.WriteAsync(crlf, 0, crlf.Length, cancelToken).ConfigureAwait(false);
                }

                await rs.WriteAsync(bodyTerminator, 0, bodyTerminator.Length, cancelToken).ConfigureAwait(false);
            }

            try
            {
                return (HttpWebResponse)(await req.GetResponseAsync().ConfigureAwait(false));
            }
            /*
             * Catch any web exceptions and grab the error detail from the respose stream
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
        }

        protected virtual HttpWebRequest PreparePostMultipart(string url, Action<HttpWebRequest> setup, string boundary, byte[] bodyTerminator, out HeaderPart[] headers, params MultipartItem[] parts)
        {
            headers =
                (from p in parts
                 select new HeaderPart(
                         Encoding.UTF8.GetBytes(
                         "--" + boundary + "\r\n"
                         + string.Join("",
                             from n in p.Headers
                             select string.Format("{0}: {1}\r\n", n.Key, n.Value)
                         ) + "\r\n"),
                         p)).ToArray();

            var envelopesize = headers.Sum(x => x.Header.Length + crlf.Length) + bodyTerminator.Length;
            var datasize = parts.Sum(x => x.ContentData.Length);

            var req = CreateRequest(url);

            req.Method = "POST";
            req.ContentType = "multipart/form-data; boundary=" + boundary;
            req.ContentLength = envelopesize + datasize;

            setup?.Invoke(req);
            return req;
        }

        private static void CreateBoundary(out string boundary, out byte[] bodyterminator)
        {
            boundary = "----DuplicatiFormBoundary" + Guid.NewGuid().ToString("N");
            bodyterminator = Encoding.UTF8.GetBytes("--" + boundary + "--");
        }


        /// <summary>
        /// Executes a web request and json-deserializes the results as the specified type
        /// </summary>
        /// <returns>The deserialized JSON data.</returns>
        /// <param name="url">The remote URL</param>
        /// <param name="setup">A callback method that can be used to customize the request, e.g. by setting the method, content-type and headers.</param>
        /// <param name="setupbodyreq">A callback method that can be used to submit data into the body of the request.</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        public virtual T GetJSONData<T>(string url, Action<HttpWebRequest> setup = null, Action<AsyncHttpRequest> setupbodyreq = null)
        {
            var req = CreateRequest(url);

            if (setup != null)
                setup(req);

            var areq = new AsyncHttpRequest(req);

            if (setupbodyreq != null)
                setupbodyreq(areq);

            return ReadJSONResponse<T>(areq);
        }

        /// <summary>
        /// Executes a web request and json-deserializes the results as the specified type
        /// </summary>
        /// <returns>The deserialized JSON data.</returns>
        /// <param name="url">The remote URL</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <param name="setup">A callback method that can be used to customize the request, e.g. by setting the method, content-type and headers.</param>
        /// <param name="setupbodyreq">A callback method that can be used to submit data into the body of the request.</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        public virtual async Task<T> GetJSONDataAsync<T>(string url, CancellationToken cancelToken, Action<HttpWebRequest> setup = null, Func<AsyncHttpRequest, CancellationToken, Task> setupbodyreq = null)
        {
            var req = CreateRequest(url);
            setup?.Invoke(req);

            var areq = new AsyncHttpRequest(req);
            if (setupbodyreq != null)
                await setupbodyreq(areq, cancelToken).ConfigureAwait(false);

            return await ReadJSONResponseAsync<T>(areq, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a web request by POST'ing the supplied object and json-deserializes the results as the specified type
        /// </summary>
        /// <returns>The deserialized JSON data.</returns>
        /// <param name="url">The remote URL</param>
        /// <param name="item">The data to json-serialize and POST in the request</param>
        /// <param name="method">Alternate HTTP method to use</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        public virtual T PostAndGetJSONData<T>(string url, object item, string method = null)
        {
            var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item));

            return GetJSONData<T>(
                url,
                req =>
                {
                    req.Method = method ?? "POST";
                    req.ContentType = "application/json; charset=utf-8";
                    req.ContentLength = data.Length;
                },

                req =>
                {
                    using(var rs = req.GetRequestStream())
                        rs.Write(data, 0, data.Length);
                }
            );
        }

        public virtual T ReadJSONResponse<T>(string url, object requestdata = null, string method = null)
        {
            if (requestdata is string)
                throw new ArgumentException("Cannot send string object as data");
            
            if (method == null && requestdata != null)
                method = "POST";
                
            return ReadJSONResponse<T>(CreateRequest(url, method), requestdata);
        }

        public virtual T ReadJSONResponse<T>(HttpWebRequest req, object requestdata = null)
        {
            return ReadJSONResponse<T>(new AsyncHttpRequest(req), requestdata);   
        }

        public virtual T ReadJSONResponse<T>(AsyncHttpRequest req, object requestdata = null)
        {
            using(var resp = GetResponse(req, requestdata))
                return ReadJSONResponse<T>(resp);
        }

        public virtual async Task<T> ReadJSONResponseAsync<T>(AsyncHttpRequest req, CancellationToken cancelToken, object requestdata = null)
        {
            using (var resp = await GetResponseAsync(req, cancelToken, requestdata).ConfigureAwait(false))
                return ReadJSONResponse<T>(resp);
        }

        public virtual T ReadJSONResponse<T>(HttpWebResponse resp)
        {
            using (var rs = Duplicati.Library.Utility.AsyncHttpRequest.TrySetTimeout(resp.GetResponseStream()))
            using(var ps = new StreamPeekReader(rs))
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
        protected virtual void ParseException(Exception ex)
        {
        }

        public HttpWebResponse GetResponseWithoutException(string url, object requestdata = null, string method = null)
        {
            if (requestdata is string)
                throw new ArgumentException("Cannot send string object as data");

            if (method == null && requestdata != null)
                method = "POST";

            return GetResponseWithoutException(CreateRequest(url, method), requestdata);
        }

        public HttpWebResponse GetResponseWithoutException(HttpWebRequest req, object requestdata = null)
        {
            return GetResponseWithoutException(new AsyncHttpRequest(req), requestdata);
        }

        public HttpWebResponse GetResponseWithoutException(AsyncHttpRequest req, object requestdata = null)
        {
            try
            {
                if (requestdata != null)
                {
                    if (requestdata is Stream stream)
                    {
                        req.Request.ContentLength = stream.Length;
                        if (string.IsNullOrEmpty(req.Request.ContentType))
                            req.Request.ContentType = "application/octet-stream";

                        using (var rs = req.GetRequestStream())
                            Library.Utility.Utility.CopyStream(stream, rs);
                    }
                    else
                    {
                        var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestdata));
                        req.Request.ContentLength = data.Length;
                        req.Request.ContentType = "application/json; charset=UTF-8";

                        using (var rs = req.GetRequestStream())
                            rs.Write(data, 0, data.Length);
                    }
                }

                return (HttpWebResponse)req.GetResponse();
            }
            catch(WebException wex)
            {
                if (wex.Response is HttpWebResponse response)
                    return response;

                /*
                 * If not an HttpWebResponse then grab the error detail from the response stream
                 */
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
        }

        public async Task<HttpWebResponse> GetResponseWithoutExceptionAsync(string url, CancellationToken cancelToken, object requestdata = null, string method = null)
        {
            if (requestdata is string)
                throw new ArgumentException("Cannot send string object as data");

            if (method == null && requestdata != null)
                method = "POST";

            return await GetResponseWithoutExceptionAsync(CreateRequest(url, method), cancelToken, requestdata).ConfigureAwait(false);
        }

        public async Task<HttpWebResponse> GetResponseWithoutExceptionAsync(HttpWebRequest req, CancellationToken cancelToken, object requestdata = null)
        {
            return await GetResponseWithoutExceptionAsync(new AsyncHttpRequest(req), cancelToken, requestdata).ConfigureAwait(false);
        }

        public async Task<HttpWebResponse> GetResponseWithoutExceptionAsync(AsyncHttpRequest req, CancellationToken cancelToken, object requestdata = null)
        {
            try
            {
                if (requestdata != null)
                {
                    if (requestdata is System.IO.Stream stream)
                    {
                        req.Request.ContentLength = stream.Length;
                        if (string.IsNullOrEmpty(req.Request.ContentType))
                            req.Request.ContentType = "application/octet-stream";

                        using (var rs = req.GetRequestStream())
                            await Utility.Utility.CopyStreamAsync(stream, rs, cancelToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestdata));
                        req.Request.ContentLength = data.Length;
                        req.Request.ContentType = "application/json; charset=UTF-8";

                        using (var rs = req.GetRequestStream())
                            await rs.WriteAsync(data, 0, data.Length, cancelToken).ConfigureAwait(false);
                    }
                }

                return (HttpWebResponse)req.GetResponse();
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse response)
                    return response;

                /*
                 * If not an HttpWebResponse then grab the error detail from the response stream
                 */
                using (Stream exstream = wex.Response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(exstream))
                    {
                        string error = reader.ReadToEnd();
                        throw new WebException(error, wex, wex.Status, wex.Response);
                    }
                }
            }
        }

        public HttpWebResponse GetResponse(string url, object requestdata = null, string method = null)
        {
            if (requestdata is string)
                throw new ArgumentException("Cannot send string object as data");

            if (method == null && requestdata != null)
                method = "POST";

            return GetResponse(CreateRequest(url, method), requestdata);
        }

        public HttpWebResponse GetResponse(HttpWebRequest req, object requestdata = null)
        {
            return GetResponse(new AsyncHttpRequest(req), requestdata);
        }

        public HttpWebResponse GetResponse(AsyncHttpRequest req, object requestdata = null)
        {
            try
            {
                if (requestdata != null)
                {
                    if (requestdata is Stream stream)
                    {
                        req.Request.ContentLength = stream.Length;
                        if (string.IsNullOrEmpty(req.Request.ContentType))
                            req.Request.ContentType = "application/octet-stream";

                        using (var rs = req.GetRequestStream())
                            Library.Utility.Utility.CopyStream(stream, rs);
                    }
                    else
                    {
                        var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestdata));
                        req.Request.ContentLength = data.Length;
                        req.Request.ContentType = "application/json; charset=UTF-8";

                        using (var rs = req.GetRequestStream())
                            rs.Write(data, 0, data.Length);
                    }
                }

                return (HttpWebResponse)req.GetResponse();
            }
            /*
             * If a web exception then grab the error detail from the response stream
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
            catch (Exception ex)
            {
                ParseException(ex);
                throw;
            }
        }

        public async Task<HttpWebResponse> GetResponseAsync(string url, CancellationToken cancelToken, object requestdata = null, string method = null)
        {
            if (requestdata is string)
                throw new ArgumentException("Cannot send string object as data");
            
            if (method == null && requestdata != null)
                method = "POST";

            var areq = new AsyncHttpRequest(CreateRequest(url, method));
            return await GetResponseAsync(areq, cancelToken, requestdata).ConfigureAwait(false);
        }

        public async Task<HttpWebResponse> GetResponseAsync(AsyncHttpRequest req, CancellationToken cancelToken, object requestdata = null)
        {
            try
            {
                if (requestdata != null)
                {
                    if (requestdata is System.IO.Stream stream)
                    {
                        req.Request.ContentLength = stream.Length;
                        if (string.IsNullOrEmpty(req.Request.ContentType))
                            req.Request.ContentType = "application/octet-stream";

                        using (var rs = req.GetRequestStream())
                            await Utility.Utility.CopyStreamAsync(stream, rs, cancelToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestdata));
                        req.Request.ContentLength = data.Length;
                        req.Request.ContentType = "application/json; charset=UTF-8";

                        using (var rs = req.GetRequestStream())
                            await rs.WriteAsync(data, 0, data.Length, cancelToken).ConfigureAwait(false);
                    }
                }

                return (HttpWebResponse)req.GetResponse();
            }
            /*
             * If a web exception then grab the error detail from the response stream
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
            catch (Exception ex)
            {
                ParseException(ex);
                throw;
            }
        }

        protected class HeaderPart
        {
            public readonly byte[] Header;
            public readonly MultipartItem Part;

            public HeaderPart(byte[] header, MultipartItem part)
            {
                Header = header;
                Part = part;
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
