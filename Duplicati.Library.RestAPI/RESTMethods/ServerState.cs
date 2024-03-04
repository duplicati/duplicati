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
using Duplicati.Library.RestAPI;
using System;
using System.Collections.Generic;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class ServerState : IRESTMethodGET, IRESTMethodPOST, IRESTMethodDocumented
    {
        public void GET(string key, RequestInfo info)
        {
            bool isError;
            long id = 0;
            long.TryParse(key, out id);

            if (info.LongPollCheck(FIXMEGlobal.StatusEventNotifyer, ref id, out isError))
            {
                //Make sure we do not report a higher number than the eventnotifier says
                var st = new Serializable.ServerStatus();
                st.LastEventID = id;
                info.OutputOK(st);
            }
            else if (!isError)
            {
                info.OutputOK(new Serializable.ServerStatus());
            }
        }

        public void POST(string key, RequestInfo info)
        {
            var input = info.Request.Form;
            switch ((key ?? "").ToLowerInvariant())
            {
                case "pause":
                    if (input.Contains("duration") && !string.IsNullOrWhiteSpace(input["duration"].Value))
                    {
                        TimeSpan ts;
                        try
                        {
                            ts = Library.Utility.Timeparser.ParseTimeSpan(input["duration"].Value);
                        }
                        catch (Exception ex)
                        {
                            info.ReportClientError(ex.Message, System.Net.HttpStatusCode.BadRequest);
                            return;
                        }
                        if (ts.TotalMilliseconds > 0)
                            FIXMEGlobal.LiveControl.Pause(ts);
                        else
                            FIXMEGlobal.LiveControl.Pause();
                    }
                    else
                    {
                        FIXMEGlobal.LiveControl.Pause();
                    }

                    info.OutputOK();
                    return;

                case "resume":
                    FIXMEGlobal.LiveControl.Resume();
                    info.OutputOK();
                    return;
                    
                default:
                    info.ReportClientError("No such action", System.Net.HttpStatusCode.NotFound);
                    return;
            }
        }

        public string Description { get { return "Return the state of the server. This method can be long-polled."; } }

        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(Serializable.ServerStatus)),
                    new KeyValuePair<string, Type>(HttpServer.Method.Post, typeof(Serializable.ServerStatus))
                };
            }
        }
    }
}

