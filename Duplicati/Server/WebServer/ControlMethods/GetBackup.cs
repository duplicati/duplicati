//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.Server.WebServer
{
    partial class ControlHandler
    {
        private void GetBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var bk = Program.DataConnection.GetBackup(input["id"].Value);
            if (bk == null)
                ReportError(response, bw, "Invalid or missing backup id");
            else
            {
                var systemIO = Library.Utility.Utility.IsClientLinux
                    ? (Duplicati.Library.Snapshots.ISystemIO)new Duplicati.Library.Snapshots.SystemIOLinux()
                    : (Duplicati.Library.Snapshots.ISystemIO)new Duplicati.Library.Snapshots.SystemIOWindows();

                var scheduleId = Program.DataConnection.GetScheduleIDsFromTags(new string[] { "ID=" + bk.ID });
                var schedule = scheduleId.Any() ? Program.DataConnection.GetSchedule(scheduleId.First()) : null;
                var sourcenames = bk.Sources.Distinct().Select(x => {
                    var sp = SpecialFolders.TranslateToDisplayString(x);
                    if (sp != null)
                        return new KeyValuePair<string, string>(x, sp);

                    x = SpecialFolders.ExpandEnvironmentVariables(x);
                    try {
                        var nx = x;
                        if (nx.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                            nx = nx.Substring(0, nx.Length - 1);
                        var n = systemIO.PathGetFileName(nx);
                        if (!string.IsNullOrWhiteSpace(n))
                            return new KeyValuePair<string, string>(x, n);
                    } catch {
                    }

                    if (x.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()) && x.Length > 1)
                        return new KeyValuePair<string, string>(x, x.Substring(0, x.Length - 1).Substring(x.Substring(0, x.Length - 1).LastIndexOf("/") + 1));
                    else
                        return new KeyValuePair<string, string>(x, x);

                }).ToDictionary(x => x.Key, x => x.Value);

                //TODO: Filter out the password in both settings and the target url

                bw.WriteJsonObject(new
                {
                    success = true,
                    data = new {
                        Schedule = schedule,
                        Backup = bk,
                        DisplayNames = sourcenames
                    }
                });
            }
        }
    }
}

