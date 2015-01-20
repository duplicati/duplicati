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
        private void ReadLogData(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            var backupid = input["id"].Value;

            if (string.IsNullOrWhiteSpace(backupid))
            {
                List<Dictionary<string, object>> res = null;
                Program.DataConnection.ExecuteWithCommand(x =>
                {
                    res = DumpTable(x, "ErrorLog", "Timestamp", input["offset"].Value, input["pagesize"].Value);
                });

                bw.OutputOK(res);
            }
            else
            {
                var backup = Program.DataConnection.GetBackup(backupid);
                if (backup == null)
                {
                    ReportError(response, bw, "Invalid or missing backup id");
                    return;
                }

                using(var con = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType))
                {
                    con.ConnectionString = "Data Source=" + backup.DBPath;
                    con.Open();

                    using(var cmd = con.CreateCommand())
                    {
                        if (Duplicati.Library.Utility.Utility.ParseBool(input["remotelog"].Value, false))
                        {
                            var dt = DumpTable(cmd, "RemoteOperation", "ID", input["offset"].Value, input["pagesize"].Value);

                            // Unwrap raw data to a string
                            foreach(var n in dt)
                                try { n["Data"] = System.Text.Encoding.UTF8.GetString((byte[])n["Data"]); }
                            catch { }

                            bw.OutputOK(dt);
                        }
                        else
                        {
                            var dt = DumpTable(cmd, "LogData", "ID", input["offset"].Value, input["pagesize"].Value);
                            bw.OutputOK(dt);
                        }
                    }
                }
            }
        }
    }
}

