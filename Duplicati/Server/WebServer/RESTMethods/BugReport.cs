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

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class BugReport : IRESTMethodGET
    {
        public void GET(string key, RequestInfo info)
        {
            long id;
            long.TryParse(key, out id);

            var tf = Program.DataConnection.GetTempFiles().FirstOrDefault(x => x.ID == id);
            if (tf == null)
            {
                info.ReportClientError("Invalid or missing bugreport id", System.Net.HttpStatusCode.NotFound);
                return;
            }

            if (!System.IO.File.Exists(tf.Path))
            {
                info.ReportClientError("File is missing", System.Net.HttpStatusCode.NotFound);
                return;
            }

            var filename = "bugreport.zip";
            using(var fs = System.IO.File.OpenRead(tf.Path))
            {
                info.Response.ContentLength = fs.Length;
                info.Response.AddHeader("Content-Disposition", string.Format("attachment; filename={0}", filename));
                info.Response.ContentType = "application/octet-stream";

                info.BodyWriter.SetOK();
                info.Response.SendHeaders();
                fs.CopyTo(info.Response.Body);
                info.Response.Send();
            }
        }
    }
}

