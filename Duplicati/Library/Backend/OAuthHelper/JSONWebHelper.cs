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
using System.Net;
using Duplicati.Library.Utility;
using Newtonsoft.Json;
using System.Text;

namespace Duplicati.Library
{
    public class JSONWebHelper
    {
        public static readonly string USER_AGENT = string.Format("Duplicati v{0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString());
        private readonly string m_user_agent;
        public string OAuthLoginUrl { get; protected set; }
        public string UserAgent { get { return m_user_agent; } }
        public event Action<HttpWebRequest> CreateSetupHelper;

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

        public T ReadJSONResponse<T>(string url, object requestdata = null, string method = null)
        {
            if (requestdata is string)
                throw new ArgumentException("Cannot send string object as data");
            
            if (method == null && requestdata != null)
                method = "POST";
                
            return ReadJSONResponse<T>(CreateRequest(url, method), requestdata);
        }

        public T ReadJSONResponse<T>(HttpWebRequest req, object requestdata = null)
        {
            return ReadJSONResponse<T>(new AsyncHttpRequest(req), requestdata);   
        }

        public T ReadJSONResponse<T>(AsyncHttpRequest req, object requestdata = null)
        {
            using(var resp = GetResponse(req, requestdata))
                return ReadJSONResponse<T>(resp);
        }

        public T ReadJSONResponse<T>(HttpWebResponse resp)
        {
            using(var rs = resp.GetResponseStream())
            using(var tr = new System.IO.StreamReader(rs))
            using(var jr = new Newtonsoft.Json.JsonTextReader(tr))
                return new Newtonsoft.Json.JsonSerializer().Deserialize<T>(jr);
        }

        public HttpWebResponse GetResponseWithoutException(string url, string method = null)
        {
            return GetResponseWithoutException(CreateRequest(url, method));
        }

        public HttpWebResponse GetResponseWithoutException(HttpWebRequest req)
        {
            return GetResponseWithoutException(new AsyncHttpRequest(req));
        }

        public HttpWebResponse GetResponseWithoutException(AsyncHttpRequest req)
        {
            try
            {
                return (HttpWebResponse)req.GetResponse();
            }
            catch(WebException wex)
            {
                if (wex.Response is HttpWebResponse)
                    return (HttpWebResponse)wex.Response;

                throw;
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
            
            if (requestdata != null)
            {
                if (requestdata is System.IO.Stream)
                {
                    var stream = requestdata as System.IO.Stream;
                    req.Request.ContentLength = stream.Length;
                    if (string.IsNullOrEmpty(req.Request.ContentType))
                        req.Request.ContentType = "application/octet-stream";

                    using(var rs = req.GetRequestStream())
                        Library.Utility.Utility.CopyStream(stream, rs);
                }
                else
                {
                    var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestdata));
                    req.Request.ContentLength = data.Length;
                    req.Request.ContentType = "application/json; charset=UTF-8";

                    using(var rs = req.GetRequestStream())
                        rs.Write(data, 0, data.Length);
                }
            }

            return (HttpWebResponse)req.GetResponse();
        }
    }
}

