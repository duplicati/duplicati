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
using Duplicati.Server.Serialization;

namespace Duplicati.Server.WebServer
{
    public class BodyWriter : System.IO.StreamWriter, IDisposable
    {
        private HttpServer.IHttpResponse m_resp;
        private string m_jsonp;
        private static object SUCCESS_RESPONSE = new { Status = "OK" };

        // We override the format provider so all JSON output uses US format
        public override IFormatProvider FormatProvider
        {
            get { return System.Globalization.CultureInfo.InvariantCulture; }
        }

        public BodyWriter(HttpServer.IHttpResponse resp, HttpServer.IHttpRequest request)
            : this(resp, request.QueryString["jsonp"].Value)
        {
        }

        public BodyWriter(HttpServer.IHttpResponse resp, string jsonp)
            : base(resp.Body,  resp.Encoding)
        {
            m_resp = resp;
            m_jsonp = jsonp;
        }

        protected override void Dispose (bool disposing)
        {
            if (!m_resp.HeadersSent)
            {
                base.Flush();
                m_resp.ContentLength = base.BaseStream.Length;
                m_resp.Send();
            }
            base.Dispose(disposing);
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

            using(this)
            {
                if (!string.IsNullOrEmpty(m_jsonp))
                {
                    this.Write(m_jsonp);
                    this.Write('(');
                }

                Serializer.SerializeJson(this, o, true);

                if (!string.IsNullOrEmpty(m_jsonp))
                {
                    this.Write(')');
                    this.Flush();
                }
            }
        }
    }

}

