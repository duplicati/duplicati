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
using System.Linq;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class ServerSetting : IRESTMethodGET, IRESTMethodPUT, IRESTMethodDocumented
    {
        public void GET(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                info.OutputError(null, System.Net.HttpStatusCode.BadRequest, "Key is missing");
                return;
            }

            if (key.Equals("server-ssl-certificate", StringComparison.OrdinalIgnoreCase) || key.Equals("ServerSSLCertificate", StringComparison.OrdinalIgnoreCase))
            {
                info.OutputOK(FIXMEGlobal.DataConnection.ApplicationSettings.ServerSSLCertificate == null ? "False" : "True");
                return;
            }

            if (key.StartsWith("--", StringComparison.Ordinal))
            {
                var prop = FIXMEGlobal.DataConnection.Settings.FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.OrdinalIgnoreCase));
                info.OutputOK(prop == null ? null : prop.Value);
            }
            else
            {
                var prop = typeof(Database.ServerSettings).GetProperty(key);
                if (prop == null)
                    info.OutputError(null, System.Net.HttpStatusCode.NotFound, "Not found");
                else
                    info.OutputOK(prop.GetValue(FIXMEGlobal.DataConnection.ApplicationSettings));
            }
        }

        public void PUT(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                info.OutputError(null, System.Net.HttpStatusCode.BadRequest, "Key is missing");
                return;
            }

            if (key.Equals("server-ssl-certificate", StringComparison.OrdinalIgnoreCase) || key.Equals("ServerSSLCertificate", StringComparison.OrdinalIgnoreCase))
            {
                info.OutputError(null, System.Net.HttpStatusCode.BadRequest, "Can only update SSL certificate from commandline");
                return;
            }

            if (key.StartsWith("--", StringComparison.Ordinal))
            {
                var settings = FIXMEGlobal.DataConnection.Settings.ToList();

                var prop = settings.FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.OrdinalIgnoreCase));
                if (prop == null)
                    settings.Add(prop = new Database.Setting() { Name = key, Value = info.Request.Form["data"].Value });
                else
                    prop.Value = info.Request.Form["data"].Value;

                FIXMEGlobal.DataConnection.Settings = settings.ToArray();
                
                info.OutputOK(prop == null ? null : prop.Value);
            }
            else
            {
                var prop = typeof(Database.ServerSettings).GetProperty(key);
                if (prop == null)
                    info.OutputError(null, System.Net.HttpStatusCode.NotFound, "Not found");
                else
                {
                    var dict = new Dictionary<string, string>();
                    dict[key] = info.Request.Form["data"].Value;
                    FIXMEGlobal.DataConnection.ApplicationSettings.UpdateSettings(dict, false);
                    info.OutputOK();
                }
            }
        }

        public string Description { get { return "Return a list of settings for the server"; } }

        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(string)),
                    new KeyValuePair<string, Type>(HttpServer.Method.Put, typeof(string))
                };
            }
        }
    }
}

