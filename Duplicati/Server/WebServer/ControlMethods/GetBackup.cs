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
                var scheduleId = Program.DataConnection.GetScheduleIDsFromTags(new string[] { "ID=" + bk.ID });
                var schedule = scheduleId.Any() ? Program.DataConnection.GetSchedule(scheduleId.First()) : null;
                var sourcenames = SpecialFolders.GetSourceNames(bk);

                //TODO: Filter out the password in both settings and the target url

                bw.OutputOK(new
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

