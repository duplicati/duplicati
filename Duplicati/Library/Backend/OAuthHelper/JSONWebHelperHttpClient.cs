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

using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpMethod = System.Net.Http.HttpMethod;

namespace Duplicati.Library;

/// <summary>
/// Minimalist version of the JSONWebHelper to be used with HttpClient HttpRequestMessage/HttpResponseMessage types
/// </summary>
/// <param name="httpClient">HttpClient reference</param>
public class JsonWebHelperHttpClient(HttpClient httpClient)
{
    /// <summary>
    /// HttpClient reference
    /// </summary>
    protected readonly HttpClient _httpClient = httpClient;

    /// <summary>
    /// Useragent string building method
    /// </summary>
    protected string UserAgent => $"Duplicati v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
    public event Action<HttpRequestMessage> CreateSetupHelper;

    /// <summary>
    /// Centralized method to prepare a request with the given URL and method setting useragent
    /// </summary>
    /// <param name="url">Url</param>
    /// <param name="method">Method</param>
    public virtual HttpRequestMessage CreateRequest(string url, string method = null)
    {
        HttpRequestMessage request = new HttpRequestMessage(string.IsNullOrEmpty(method) ? HttpMethod.Get : new HttpMethod(method), url);
        request.Headers.Add("User-Agent", UserAgent);

        CreateSetupHelper?.Invoke(request);

        return request;
    }

    /// <summary>
    /// Execute Get request and return response and deserializes JSON response into the given type
    /// </summary>
    /// <param name="url">Url</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <param name="setup">Setup Actions for customizing the request</param>
    /// <typeparam name="T">Destination Type</typeparam>
    protected virtual async Task<T> GetJsonDataAsync<T>(string url, CancellationToken cancellationToken, Action<HttpRequestMessage> setup = null)
    {
        var req = CreateRequest(url);

        if (setup != null)
            setup(req);

        return await ReadJsonResponseAsync<T>(req, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute Post and return response and deserializes JSON response into the given type
    /// </summary>
    /// <param name="url">Url</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <param name="item">The item to be serialized into a json and added to the body</param>
    /// <typeparam name="T">Destination Type</typeparam>
    public virtual async Task<T> PostAndGetJsonDataAsync<T>(string url, object item, CancellationToken cancellationToken)
    {
        var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item));

        return await GetJsonDataAsync<T>(
            url,
            cancellationToken,
            request =>
            {
                request.Method = HttpMethod.Post;
                request.Content = new ByteArrayContent(data);
                request.Content.Headers.Add("Content-Length", data.Length.ToString());
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the JSON response from the server and deserializes it into the given type
    /// </summary>
    /// <param name="req">Request object</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <typeparam name="T">Destination Type</typeparam>
    protected virtual async Task<T> ReadJsonResponseAsync<T>(HttpRequestMessage req, CancellationToken cancellationToken)
    {
        using var resp = await GetResponseAsync(req, cancellationToken).ConfigureAwait(false);
        return await ReadJsonResponsAsync<T>(resp, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read the JSON response from the server and deserialize it into the given type
    /// </summary>
    /// <param name="response">Response object</param>
    /// <typeparam name="T">Type to cast to</typeparam>
    /// <returns></returns>
    /// <exception cref="IOException">Exception when failing to deserialize the JSON to the Type</exception>
    protected virtual T ReadJsonResponse<T>(HttpResponseMessage response)
    {
        using var rs = response.Content.ReadAsStream();
        using var ps = new StreamPeekReader(rs);
        try
        {
            using var tr = new StreamReader(ps);
            using var jr = new JsonTextReader(tr);
            return new JsonSerializer().Deserialize<T>(jr);
        }
        catch (Exception ex)
        {
            // If we get invalid JSON, report the peek value
            if (ex is JsonReaderException)
                throw new IOException($"Invalid JSON data: \"{ps.PeekData()}\"", ex);
            // Otherwise, we have no additional help to offer
            throw;
        }
    }

    /// <summary>
    /// Read the JSON response from the server and deserialize it into the given type asynchronously
    /// </summary>
    /// <param name="response">Response object</param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="T">Type to cast to</typeparam>
    /// <returns></returns>
    /// <exception cref="IOException">Exception when failing to deserialize the JSON to the Type</exception>
    protected virtual async Task<T> ReadJsonResponsAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var rs = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var ps = new StreamPeekReader(rs);
        try
        {
            using var tr = new StreamReader(ps);
            await using var jr = new JsonTextReader(tr);
            return new JsonSerializer().Deserialize<T>(jr);
        }
        catch (Exception ex)
        {
            // If we get invalid JSON, report the peek value
            if (ex is JsonReaderException)
                throw new IOException($"Invalid JSON data: \"{ps.PeekData()}\"", ex);
            // Otherwise, we have no additional help to offer
            throw;
        }
    }

    /// <summary>
    /// Use this method to register an exception handler,
    /// which can throw another, more meaningful exception
    /// </summary>
    public virtual void AttemptParseAndThrowException(Exception ex, HttpResponseMessage responseContext = null)
    {

    }

    /// <summary>
    /// Execute request and return response
    /// </summary>
    /// <param name="req">Request object</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns></returns>
    private async Task<HttpResponseMessage> GetResponseAsync(HttpRequestMessage req, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = null;
        try
        {
            response = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch (Exception ex)
        {
            AttemptParseAndThrowException(ex, response);
            throw;
        }
    }

    /// <summary>
    /// A utility class that shadows the real stream but provides access
    /// to the first 2kb of the stream to use in error reporting
    /// </summary>
    private class StreamPeekReader(Stream source) : Stream
    {
        private readonly byte[] m_peekbuffer = new byte[1024 * 2];
        private int m_peekbytes = 0;

        public string PeekData()
        {
            if (m_peekbuffer.Length == 0)
                return string.Empty;

            return Encoding.UTF8.GetString(m_peekbuffer, 0, m_peekbytes);
        }

        public override bool CanRead => source.CanRead;
        public override bool CanSeek => source.CanSeek;
        public override bool CanWrite => source.CanWrite;
        public override long Length => source.Length;
        public override long Position { get => source.Position; set => source.Position = value; }
        public override void Flush() => source.Flush();
        public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
        public override void SetLength(long value) => source.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => source.Write(buffer, offset, count);
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) => source.BeginRead(buffer, offset, count, callback, state);
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state) => source.BeginWrite(buffer, offset, count, callback, state);
        public override bool CanTimeout => source.CanTimeout;
        public override void Close() => source.Close();
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => source.CopyToAsync(destination, bufferSize, cancellationToken);
        protected override void Dispose(bool disposing) => base.Dispose(disposing);
        public override int EndRead(IAsyncResult asyncResult) => source.EndRead(asyncResult);
        public override void EndWrite(IAsyncResult asyncResult) => source.EndWrite(asyncResult);
        public override Task FlushAsync(CancellationToken cancellationToken) => source.FlushAsync(cancellationToken);
        public override int ReadTimeout { get => source.ReadTimeout; set => source.ReadTimeout = value; }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => source.WriteAsync(buffer, offset, count, cancellationToken);
        public override int WriteTimeout { get => source.WriteTimeout; set => source.WriteTimeout = value; }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var br = 0;
            if (m_peekbytes < m_peekbuffer.Length - 1)
            {
                var maxb = Math.Min(count, m_peekbuffer.Length - m_peekbytes);
                br = await source.ReadAsync(m_peekbuffer, m_peekbytes, maxb, cancellationToken);
                Array.Copy(m_peekbuffer, m_peekbytes, buffer, offset, br);
                m_peekbytes += br;
                offset += br;
                count -= br;
                if (count == 0 || br < maxb)
                    return br;
            }

            return await source.ReadAsync(buffer, offset, count, cancellationToken) + br;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var br = 0;
            if (m_peekbytes < m_peekbuffer.Length - 1)
            {
                var maxb = Math.Min(count, m_peekbuffer.Length - m_peekbytes);
                br = source.Read(m_peekbuffer, m_peekbytes, maxb);
                Array.Copy(m_peekbuffer, m_peekbytes, buffer, offset, br);
                m_peekbytes += br;
                offset += br;
                count -= br;

                if (count == 0 || br < maxb)
                    return br;
            }

            return source.Read(buffer, offset, count) + br;
        }

    }
}