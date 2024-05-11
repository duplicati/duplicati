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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Server.Database;
using System.IO;
using Duplicati.Server.Serialization;
using Duplicati.Library.RestAPI;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class ServerSettings : IRESTMethodGET, IRESTMethodPATCH, IRESTMethodDocumented
    {
        public void GET(string key, RequestInfo info)
        {
            // Join server settings and global settings
            var adv_props = 
                FIXMEGlobal.DataConnection.GetSettings(Database.Connection.SERVER_SETTINGS_ID)
                       .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                       .Union(
                           FIXMEGlobal.DataConnection.Settings
                           .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Name.StartsWith("--", StringComparison.Ordinal))
                      );

            var dict = new Dictionary<string, string>();
            foreach (var n in adv_props)
                dict[n.Name] = n.Value;

            string sslcert;
            dict.TryGetValue("server-ssl-certificate", out sslcert);
            dict["server-ssl-certificate"] = (!string.IsNullOrWhiteSpace(sslcert)).ToString();

            info.OutputOK(dict);
        }

        public void PATCH(string key, RequestInfo info)
        {
            string str = info.Request.Form["data"].Value;

            if (string.IsNullOrWhiteSpace(str))
                str = System.Threading.Tasks.Task.Run(async () =>
                {
                    using (var sr = new System.IO.StreamReader(info.Request.Body, System.Text.Encoding.UTF8, true))

                        return await sr.ReadToEndAsync();
                }).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(str))
            {
                info.ReportClientError("Missing data object", System.Net.HttpStatusCode.BadRequest);
                return;
            }

            Dictionary<string, string> data = null;
            try
            {
                data = Serializer.Deserialize<Dictionary<string, string>>(new StringReader(str));
                if (data == null)
                {
                    info.ReportClientError("Data object had no entry", System.Net.HttpStatusCode.BadRequest);
                    return;
                }

                // Split into server settings and global settings

                var serversettings = data.Where(x => !string.IsNullOrWhiteSpace(x.Key)).ToDictionary(x => x.Key, x => x.Key.StartsWith("--", StringComparison.Ordinal) ? null : x.Value);
                var globalsettings = data.Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Key.StartsWith("--", StringComparison.Ordinal));

                serversettings.Remove("server-ssl-certificate");
				serversettings.Remove("ServerSSLCertificate");

                if (serversettings.Any())
				    FIXMEGlobal.DataConnection.ApplicationSettings.UpdateSettings(serversettings, false);

                if (globalsettings.Any())
                {
                    // Update based on inputs
                    var existing = FIXMEGlobal.DataConnection.Settings.ToDictionary(x => x.Name, x => x);
                    foreach (var g in globalsettings)
                        if (g.Value == null)
                            existing.Remove(g.Key);
                        else
                        {
                            if (existing.ContainsKey(g.Key))
                                existing[g.Key].Value = g.Value;
                            else
                                existing[g.Key] = new Setting() { Name = g.Key, Value = g.Value };
                        }

                    FIXMEGlobal.DataConnection.Settings = existing.Select(x => x.Value).ToArray();
                }

                info.OutputOK();
            }
            catch (Exception ex)
            {
                if (data == null)
                    info.ReportClientError(string.Format("Unable to parse data object: {0}", ex.Message), System.Net.HttpStatusCode.BadRequest);
                else
                    info.ReportClientError(string.Format("Unable to save settings: {0}", ex.Message), System.Net.HttpStatusCode.InternalServerError);
            }            
        }

        public string Description { get { return "Return a list of settings for the server"; } }

        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(Database.ServerSettings))
                };
            }
        }    
    }
}

