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

namespace Duplicati.Server.WebServer
{
    partial class ControlHandler
    {
        private void SearchBackupFiles(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

            var filter = (input["filter"].Value ?? "").Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);
            var timestring = input["time"].Value;
            var allversion = Duplicati.Library.Utility.Utility.ParseBool(input["all-versions"].Value, false);

            if (string.IsNullOrWhiteSpace(timestring) && !allversion)
            {
                ReportError(response, bw, "Invalid or missing time");
                return;
            }
            var bk = Program.DataConnection.GetBackup(input["id"].Value);
            if (bk == null)
            {
                ReportError(response, bw, "Invalid or missing backup id");
                return;
            }

            var prefixonly = Duplicati.Library.Utility.Utility.ParseBool(input["prefix-only"].Value, false);
            var foldercontents = Duplicati.Library.Utility.Utility.ParseBool(input["folder-contents"].Value, false);
            var time = new DateTime();
            if (!allversion)
                time = Duplicati.Library.Utility.Timeparser.ParseTimeInterval(timestring, DateTime.Now);

            var r = Runner.Run(Runner.CreateListTask(bk, filter, prefixonly, allversion, foldercontents, time), false) as Duplicati.Library.Interface.IListResults;

            var result = new Dictionary<string, object>();

            foreach(HttpServer.HttpInputItem n in input)
                result[n.Name] = n.Value;

            result["Filesets"] = r.Filesets;
            result["Files"] = r.Files;

            bw.OutputOK(result);
        }
    }
}

