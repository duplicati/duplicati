// Copyright (C) 2024, The Duplicati Team
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
using System.Globalization;
using System.Threading.Tasks;
using Duplicati.Server.Serialization;

namespace Duplicati.Server.WebServer
{
    public class BodyWriter : IDisposable, IAsyncDisposable
    {
        private readonly HttpServer.IHttpResponse m_resp;
        private readonly string m_jsonp;
        private static readonly object SUCCESS_RESPONSE = new { Status = "OK" };
        private readonly System.IO.StreamWriter m_bodyStreamWriter;

        public BodyWriter(HttpServer.IHttpResponse resp, HttpServer.IHttpRequest request)
            : this(resp, request.QueryString["jsonp"].Value)
        {
        }

        public BodyWriter(HttpServer.IHttpResponse resp, string jsonp)
        {
            m_bodyStreamWriter = new System.IO.StreamWriter(resp.Body, resp.Encoding);
            m_resp = resp;
            m_jsonp = jsonp;
            if (!m_resp.HeadersSent)
                m_resp.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
        }
        public void SetOK()
        {
            m_resp.Reason = "OK";
            m_resp.Status = System.Net.HttpStatusCode.OK;
        }

        public void OutputOK(object result = null)
        {
            SetOK();
            WriteJsonObject(result ?? SUCCESS_RESPONSE);
        }

        public void WriteJsonObject(object o)
        {
            if (!m_resp.HeadersSent)
                m_resp.ContentType = "application/json";

                Task.Run(async () => {

                        if (!string.IsNullOrEmpty(m_jsonp))
                        {
                            await m_bodyStreamWriter.WriteAsync(m_jsonp);
                            await m_bodyStreamWriter.WriteAsync('(');
                        }

                        var oldCulture = CultureInfo.CurrentCulture;
                        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                        await Serializer.SerializeJsonAsync(m_bodyStreamWriter, o, true);
                        CultureInfo.CurrentCulture = oldCulture;

                        if (!string.IsNullOrEmpty(m_jsonp))
                        {
                            await m_bodyStreamWriter.WriteAsync(')');
                            await m_bodyStreamWriter.FlushAsync();
                        }
                }).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Task.Run(async () => {
                await DisposeAsync();
            }).GetAwaiter().GetResult();
        }

        private bool disposed = false;
        public async ValueTask DisposeAsync()
        {
            if(disposed) return;
            disposed = true;

            if (!m_resp.HeadersSent)
            {
                await m_bodyStreamWriter.FlushAsync();
                m_resp.ContentLength = m_bodyStreamWriter.BaseStream.Length;
                m_resp.Send();
            }
            await m_bodyStreamWriter.DisposeAsync();
        }

        internal void Flush()
        {
            Task.Run(async () => {
                await m_bodyStreamWriter.FlushAsync();
            }).GetAwaiter().GetResult();
        }

        internal void Write(string v)
        {
            Task.Run(async () => {
                 await m_bodyStreamWriter.WriteAsync(v);
            }).GetAwaiter().GetResult();
        }
    }

}

