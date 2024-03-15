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
using System.Linq;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class BugReport : IRESTMethodGET
    {
        public void GET(string key, RequestInfo info)
        {
            long id;
            long.TryParse(key, out id);

            var tf = FIXMEGlobal.DataConnection.GetTempFiles().FirstOrDefault(x => x.ID == id);
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

