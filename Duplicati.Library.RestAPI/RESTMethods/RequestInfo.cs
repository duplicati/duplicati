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

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class RequestInfo : IDisposable
    {
        public HttpServer.IHttpRequest Request { get; private set; }
        public HttpServer.IHttpResponse Response { get; private set; }
        public HttpServer.Sessions.IHttpSession Session { get; private set; }
        public BodyWriter BodyWriter { get; private set; }
        public RequestInfo(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {
            Request = request;
            Response = response;
            Session = session;
            BodyWriter = new BodyWriter(response, request);
        }

        public void ReportServerError(string message, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.InternalServerError)
        {
            Response.Status = code;
            Response.Reason = message;

            BodyWriter.WriteJsonObject(new { Error = message });
        }

        public void ReportClientError(string message, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.BadRequest)
        {
            ReportServerError(message, code);
        }

        public bool LongPollCheck(EventPollNotify poller, ref long id, out bool isError)
        {
            HttpServer.HttpInput input = String.Equals(Request.Method, "POST", StringComparison.OrdinalIgnoreCase) ? Request.Form : Request.QueryString;
            if (Library.Utility.Utility.ParseBool(input["longpoll"].Value, false))
            {
                long lastEventId;
                if (!long.TryParse(input["lasteventid"].Value, out lastEventId))
                {
                    ReportClientError("When activating long poll, the request must include the last event id", System.Net.HttpStatusCode.BadRequest);
                    isError = true;
                    return false;
                }

                TimeSpan ts;
                try { ts = Library.Utility.Timeparser.ParseTimeSpan(input["duration"].Value); }
                catch (Exception ex)
                {
                    ReportClientError("Invalid duration: " + ex.Message, System.Net.HttpStatusCode.BadRequest);
                    isError = true;
                    return false;
                }

                if (ts <= TimeSpan.FromSeconds(10) || ts.TotalMilliseconds > int.MaxValue)
                {
                    ReportClientError("Invalid duration, must be at least 10 seconds, and less than " + int.MaxValue + " milliseconds", System.Net.HttpStatusCode.BadRequest);
                    isError = true;
                    return false;
                }

                isError = false;
                id = poller.Wait(lastEventId, (int)ts.TotalMilliseconds);
                return true;
            }

            isError = false;
            return false;
        }

        public void OutputOK(object item = null)
        {
            BodyWriter.OutputOK(item);
        }

        public void OutputError(object item = null, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.InternalServerError, string reason = null)
        {
            Response.Status = code;
            Response.Reason = reason ?? "Error";
            if(item == null && reason != null)
            {
                item = new { Error = reason };
            }
            BodyWriter.WriteJsonObject(item);
        }

        public void Dispose()
        {
            if (BodyWriter != null)
            {
                var bw = BodyWriter;
                BodyWriter = null;
                bw.Dispose();
            }
        }
    }
}

