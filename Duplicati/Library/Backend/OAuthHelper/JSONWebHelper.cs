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
using System.Linq;
using System.Net;
using Duplicati.Library.Utility;
using Newtonsoft.Json;
using System.Text;
using System.IO;
using System.Collections.Generic;

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
        /// Performs a multipart post
        /// </summary>
        /// <returns>The response.</returns>
        /// <param name="url">The url to post to.</param>
        /// <param name="parts">The multipart items.</param>
        /// <param name="setup">The optional setup callback method.</param>
        public virtual HttpWebResponse PostMultipart(string url, Action<HttpWebRequest> setup = null, params MultipartItem[] parts)
        {
            var boundary = "----DuplicatiFormBoundary" + Guid.NewGuid().ToString("N");

            var bodyterminator = System.Text.Encoding.UTF8.GetBytes("--" + boundary + "--");
            var crlf = System.Text.Encoding.UTF8.GetBytes("\r\n");

            var headers = 
                (from p in parts
                    select new {
                        Header = System.Text.Encoding.UTF8.GetBytes(
                            "--" + boundary + "\r\n" 
                            + string.Join("", 
                                from n in p.Headers 
                                select string.Format("{0}: {1}\r\n", n.Key, n.Value)
                            )
                        + "\r\n"),
                        Part = p
                }).ToArray();

            var envelopesize = headers.Sum(x => x.Header.Length + crlf.Length) + bodyterminator.Length;
            var datasize = parts.Sum(x => x.ContentData.Length);

            var req = CreateRequest(url);

            req.Method = "POST";
            req.ContentType = "multipart/form-data; boundary=" + boundary;
            req.ContentLength = envelopesize + datasize;

            if (setup != null)
                setup(req);

            var areq = new AsyncHttpRequest(req);

            using(var rs = areq.GetRequestStream())
            {

                foreach(var p in headers)
                {
                    rs.Write(p.Header, 0, p.Header.Length);
                    Utility.Utility.CopyStream(p.Part.ContentData, rs);
                    rs.Write(crlf, 0, crlf.Length);
                }

                rs.Write(bodyterminator, 0, bodyterminator.Length);
            }

            return GetResponse(areq);
        }


        /// <summary>
        /// Executes a web request and json-deserializes the results as the specified type
        /// </summary>
        /// <returns>The deserialized JSON data.</returns>
        /// <param name="url">The remote URL</param>
        /// <param name="setup">A callback method that can be used to customize the request, e.g. by setting the method, content-type and headers.</param>
        /// <param name="setupreq">A callback method that can be used to submit data into the body of the request.</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        public virtual T GetJSONData<T>(string url, Action<HttpWebRequest> setup = null, Action<AsyncHttpRequest> setupreq = null)
        {
            var req = CreateRequest(url);

            if (setup != null)
                setup(req);

            var areq = new AsyncHttpRequest(req);

            if (setupreq != null)
                setupreq(areq);

            return ReadJSONResponse<T>(areq);
        }

        /// <summary>
        /// Executes a web request by POST'ing the supplied object and json-deserializes the results as the specified type
        /// </summary>
        /// <returns>The deserialized JSON data.</returns>
        /// <param name="url">The remote URL</param>
        /// <param name="item">The data to json-serialize and POST in the request</param>
        /// <param name="setup">A callback method that can be used to customize the request, e.g. by setting the method, content-type and headers.</param>
        /// <param name="setupreq">A callback method that can be used to submit data into the body of the request.</param>
        /// <typeparam name="T">The type of data to return.</typeparam>
        public virtual T PostAndGetJSONData<T>(string url, object item, Action<HttpWebRequest> setup = null, Action<AsyncHttpRequest> setupreq = null)
        {
            var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item));

            return GetJSONData<T>(
                url,
                req =>
                {
                    req.Method = "POST";
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

        public virtual T ReadJSONResponse<T>(HttpWebResponse resp)
        {
            using(var rs = Duplicati.Library.Utility.AsyncHttpRequest.TrySetTimeout(resp.GetResponseStream()))
            using(var tr = new System.IO.StreamReader(rs))
            using(var jr = new Newtonsoft.Json.JsonTextReader(tr))
                return new Newtonsoft.Json.JsonSerializer().Deserialize<T>(jr);
        }

        /// <summary>
        /// Use this method to register an exception handler,
        /// which can throw another, more meaningful exception
        /// </summary>
        /// <param name="ex">The exception being processed.</param>
        protected virtual void ParseException(Exception ex)
        {
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
            try
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
            catch(Exception ex)
            {
                ParseException(ex);
                throw;
            }
        }
    }
}

