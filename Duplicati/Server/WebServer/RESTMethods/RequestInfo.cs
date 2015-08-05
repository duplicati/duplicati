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

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class RequestInfo : IDisposable
    {        public HttpServer.IHttpRequest Request { get; private set; }        public HttpServer.IHttpResponse Response { get; private set; }        public HttpServer.Sessions.IHttpSession Session { get; private set; }        public BodyWriter BodyWriter { get; private set; }
        public RequestInfo(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
        {            Request = request;            Response = response;            Session = session;            BodyWriter = new BodyWriter(response, request);
        }        public void ReportServerError(string message, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.InternalServerError)        {            Response.Status = System.Net.HttpStatusCode.InternalServerError;            Response.Reason = message;            BodyWriter.WriteJsonObject(new { Error = message });        }        public void ReportClientError(string message, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.BadRequest)        {            ReportServerError(message, code);        }        public bool LongPollCheck(EventPollNotify poller, ref long id, out bool isError)        {            HttpServer.HttpInput input = Request.Method.ToUpper() == "POST" ? Request.Form : Request.QueryString;            if (Library.Utility.Utility.ParseBool(input["longpoll"].Value, false))            {                long lastEventId;                if (!long.TryParse(input["lasteventid"].Value, out lastEventId))                {                    ReportClientError("When activating long poll, the request must include the last event id");                    isError = true;                    return false;                }                TimeSpan ts;                try { ts = Library.Utility.Timeparser.ParseTimeSpan(input["duration"].Value); }                catch (Exception ex)                {                    ReportClientError("Invalid duration: " + ex.Message);                    isError = true;                    return false;                }                if (ts <= TimeSpan.FromSeconds(10) || ts.TotalMilliseconds > int.MaxValue)                {                    ReportClientError("Invalid duration, must be at least 10 seconds, and less than " + int.MaxValue + " milliseconds");                    isError = true;                    return false;                }                isError = false;                id = poller.Wait(lastEventId, (int)ts.TotalMilliseconds);                return true;            }            isError = false;            return false;        }        public void OutputOK(object item = null)        {            BodyWriter.OutputOK(item);        }        public void OutputError(object item = null, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.InternalServerError, string reason = null)        {            Response.Status = code;            Response.Reason = reason ?? "Error";            BodyWriter.WriteJsonObject(item);        }        public void Dispose()        {            if (BodyWriter != null)            {                var bw = BodyWriter;                BodyWriter = null;                bw.Dispose();            }        }    }
}

