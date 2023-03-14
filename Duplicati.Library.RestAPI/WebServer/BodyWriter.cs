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

