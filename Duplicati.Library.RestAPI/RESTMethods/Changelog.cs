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
    public class Changelog : IRESTMethodGET, IRESTMethodDocumented
    {
        private class GetResponse
        {
            public string Status;
            public string Version;
            public string Changelog;
        }

        public void GET(string key, RequestInfo info)
        {
            var fromUpdate = info.Request.QueryString["from-update"].Value;
            if (!Library.Utility.Utility.ParseBool(fromUpdate, false))
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "changelog.txt");
                info.OutputOK(new GetResponse() {
                    Status = "OK",
                    Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    Changelog = System.IO.File.ReadAllText(path)
                });
            }
            else
            {
                var updateInfo = FIXMEGlobal.DataConnection.ApplicationSettings.UpdatedVersion;
                if (updateInfo == null)
                {
                    info.ReportClientError("No update found", System.Net.HttpStatusCode.NotFound);
                }
                else
                {
                    info.OutputOK(new GetResponse() {
                        Status = "OK",
                        Version = updateInfo.Version,
                        Changelog = updateInfo.ChangeInfo
                    });
                }
            }
        }

        public string Description { get { return "Gets the current changelog"; } }

        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(GetResponse)),
                };
            }
        }
    }
}

