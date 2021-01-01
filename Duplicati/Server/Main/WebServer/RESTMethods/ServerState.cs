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

            if (info.LongPollCheck(Program.StatusEventNotifyer, ref id, out isError))
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
                            Program.LiveControl.Pause(ts);
                        else
                            Program.LiveControl.Pause();
                    }
                    else
                    {
                        Program.LiveControl.Pause();
                    }

                    info.OutputOK();
                    return;

                case "resume":
                    Program.LiveControl.Resume();
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

