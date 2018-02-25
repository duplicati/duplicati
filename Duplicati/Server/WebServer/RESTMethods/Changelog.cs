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
                var updateInfo = Program.DataConnection.ApplicationSettings.UpdatedVersion;
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

