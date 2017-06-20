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
using System.Collections.Generic;
using Duplicati.Server.Database;
using System.IO;
using Duplicati.Server.Serialization;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class ServerSettings : IRESTMethodGET, IRESTMethodPATCH, IRESTMethodDocumented
    {
        public void GET(string key, RequestInfo info)
        {
            // Join server settings and global settings
            var adv_props = 
                Program.DataConnection.GetSettings(Database.Connection.SERVER_SETTINGS_ID)
                       .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                       .Union(
                           Program.DataConnection.Settings
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
                str = new StreamReader(info.Request.Body, System.Text.Encoding.UTF8).ReadToEnd();

            if (string.IsNullOrWhiteSpace(str))
            {
                info.ReportClientError("Missing data object");
                return;
            }

            Dictionary<string, string> data = null;
            try
            {
                data = Serializer.Deserialize<Dictionary<string, string>>(new StringReader(str));
                if (data == null)
                {
                    info.ReportClientError("Data object had no entry");
                    return;
                }

                // Split into server settings and global settings

                var serversettings = data.Where(x => !string.IsNullOrWhiteSpace(x.Key)).ToDictionary(x => x.Key, x => x.Key.StartsWith("--", StringComparison.Ordinal) ? null : x.Value);
                var globalsettings = data.Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Key.StartsWith("--", StringComparison.Ordinal));

                serversettings.Remove("server-ssl-certificate");
				serversettings.Remove("ServerSSLCertificate");

                if (serversettings.Any())
				    Program.DataConnection.ApplicationSettings.UpdateSettings(serversettings, false);

                if (globalsettings.Any())
                {
                    // Update based on inputs
                    var existing = Program.DataConnection.Settings.ToDictionary(x => x.Name, x => x);
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

                    Program.DataConnection.Settings = existing.Select(x => x.Value).ToArray();
                }

                info.OutputOK();
            }
            catch (Exception ex)
            {
                if (data == null)
                    info.ReportClientError(string.Format("Unable to parse data object: {0}", ex.Message));
                else
                    info.ReportClientError(string.Format("Unable to save settings: {0}", ex.Message));
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

