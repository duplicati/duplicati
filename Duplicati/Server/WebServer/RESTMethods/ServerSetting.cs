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

            if (key.Equals("server-ssl-certificate", StringComparison.InvariantCultureIgnoreCase) || key.Equals("ServerSSLCertificate", StringComparison.InvariantCultureIgnoreCase))
            {
                info.OutputOK(Program.DataConnection.ApplicationSettings.ServerSSLCertificate == null ? "False" : "True");
                return;
            }

            if (key.StartsWith("--", StringComparison.Ordinal))
            {
                var prop = Program.DataConnection.Settings.FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.InvariantCultureIgnoreCase));
                info.OutputOK(prop == null ? null : prop.Value);
            }
            else
            {
                var prop = typeof(Database.ServerSettings).GetProperty(key);
                if (prop == null)
                    info.OutputError(null, System.Net.HttpStatusCode.NotFound, "Not found");
                else
                    info.OutputOK(prop.GetValue(Program.DataConnection.ApplicationSettings));
            }
        }

        public void PUT(string key, RequestInfo info)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                info.OutputError(null, System.Net.HttpStatusCode.BadRequest, "Key is missing");
                return;
            }

			if (key.Equals("server-ssl-certificate", StringComparison.InvariantCultureIgnoreCase) || key.Equals("ServerSSLCertificate", StringComparison.InvariantCultureIgnoreCase))
			{
				info.OutputError(null, System.Net.HttpStatusCode.BadRequest, "Can only update SSL certificate from commandline");
				return;
			}

			if (key.StartsWith("--", StringComparison.Ordinal))
            {
                var settings = Program.DataConnection.Settings.ToList();

                var prop = settings.Where(x => string.Equals(key, x.Name, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                if (prop == null)
                    settings.Add(prop = new Database.Setting() { Name = key, Value = info.Request.Form["data"].Value });
                else
                    prop.Value = info.Request.Form["data"].Value;

                Program.DataConnection.Settings = settings.ToArray();
                
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
                    Program.DataConnection.ApplicationSettings.UpdateSettings(dict, false);
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

